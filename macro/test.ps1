param(
    [string] $Target = "",

    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release",

    [switch] $NoBuild,

    [switch] $NoRestore,

    [int] $HeartbeatSeconds = 5,

    [int] $StallWarningSeconds = 30,

    [string[]] $AdditionalArguments = @()
)

$ErrorActionPreference = "Stop"

$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$ResolvedTarget =
    if ([string]::IsNullOrWhiteSpace($Target)) {
        Join-Path $RepoRoot "WhbDesign.sln"
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $Target))
    }

$RunRoot = Join-Path $RepoRoot ("temp/test_runner_" + [DateTime]::UtcNow.ToString("yyyyMMdd_HHmmss_fff"))
$StdOutPath = Join-Path $RunRoot "stdout.log"
$StdErrPath = Join-Path $RunRoot "stderr.log"

function New-RunDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Get-TestArguments {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ResolvedTargetPath,
        [Parameter(Mandatory = $true)]
        [string] $BuildConfiguration,
        [switch] $SkipBuild,
        [switch] $SkipRestore,
        [string[]] $ExtraArguments
    )

    $arguments = @(
        "test"
        $ResolvedTargetPath
        "--configuration"
        $BuildConfiguration
    )

    if ($SkipBuild) {
        $arguments += "--no-build"
    }

    if ($SkipRestore) {
        $arguments += "--no-restore"
    }

    if ($ExtraArguments) {
        $arguments += $ExtraArguments
    }

    return $arguments
}

function Start-TestProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments,
        [Parameter(Mandatory = $true)]
        [string] $WorkingDirectory,
        [Parameter(Mandatory = $true)]
        [string] $OutputPath,
        [Parameter(Mandatory = $true)]
        [string] $ErrorPath
    )

    return Start-Process `
        -FilePath "dotnet" `
        -ArgumentList $Arguments `
        -WorkingDirectory $WorkingDirectory `
        -NoNewWindow `
        -RedirectStandardOutput $OutputPath `
        -RedirectStandardError $ErrorPath `
        -PassThru
}

function New-StreamState {
    return @{
        Position = 0L
        Buffer = ""
        LinesRead = 0
    }
}

function Write-AppendedLines {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,
        [Parameter(Mandatory = $true)]
        [hashtable] $State,
        [string] $Prefix = "",
        [switch] $FlushPartial
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return @()
    }

    $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)

    try {
        $stream.Seek($State.Position, [System.IO.SeekOrigin]::Begin) | Out-Null
        $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8, $true, 1024, $true)
        $chunk = $reader.ReadToEnd()
        $State.Position = $stream.Position
    }
    finally {
        $stream.Dispose()
    }

    if ([string]::IsNullOrEmpty($chunk) -and (-not $FlushPartial)) {
        return @()
    }

    $text = ($State.Buffer + $chunk).Replace("`r`n", "`n").Replace("`r", "`n")
    $parts = $text.Split(@("`n"), [System.StringSplitOptions]::None)
    $hasTrailingNewLine = $text.EndsWith("`n", [System.StringComparison]::Ordinal)

    $completeCount =
        if ($hasTrailingNewLine) {
            $parts.Length - 1
        }
        else {
            [Math]::Max(0, $parts.Length - 1)
        }

    $lines =
        if ($completeCount -gt 0) {
            $parts[0..($completeCount - 1)]
        }
        else {
            @()
        }

    $State.Buffer =
        if ($hasTrailingNewLine) {
            ""
        }
        elseif ($parts.Length -gt 0) {
            $parts[$parts.Length - 1]
        }
        else {
            ""
        }

    if ($FlushPartial -and $State.Buffer.Length -gt 0) {
        $lines += $State.Buffer
        $State.Buffer = ""
    }

    foreach ($line in $lines) {
        if (-not [string]::IsNullOrWhiteSpace($line)) {
            Write-Host ($Prefix + $line)
            $State.LinesRead += 1
        }
    }

    return $lines
}

function Get-ProcessTreeSnapshot {
    param(
        [Parameter(Mandatory = $true)]
        [int] $RootProcessId,
        [Parameter(Mandatory = $true)]
        [object[]] $ProcessTable
    )

    $byParent = @{}

    foreach ($entry in $ProcessTable) {
        $parentKey = [string] $entry.ParentProcessId
        if (-not $byParent.ContainsKey($parentKey)) {
            $byParent[$parentKey] = New-Object System.Collections.ArrayList
        }

        [void] $byParent[$parentKey].Add($entry)
    }

    $pending = @($RootProcessId)
    $visited = @{}
    $collected = New-Object System.Collections.ArrayList

    while ($pending.Count -gt 0) {
        $current = [int] $pending[0]
        $pending =
            if ($pending.Count -gt 1) {
                @($pending[1..($pending.Count - 1)])
            }
            else {
                @()
            }

        if ($visited.ContainsKey($current)) {
            continue
        }

        $visited[$current] = $true

        $currentKey = [string] $current
        if (-not $byParent.ContainsKey($currentKey)) {
            continue
        }

        foreach ($child in $byParent[$currentKey]) {
            [void] $collected.Add($child)
            $pending += [int] $child.ProcessId
        }
    }

    return @($collected)
}

function Get-ActiveProcessSnapshot {
    param(
        [Parameter(Mandatory = $true)]
        [int] $RootProcessId
    )

    $processTable = @(Get-CimInstance Win32_Process | Select-Object ProcessId, ParentProcessId, Name, CommandLine)
    $tree = Get-ProcessTreeSnapshot -RootProcessId $RootProcessId -ProcessTable $processTable
    $treeIds = @($RootProcessId) + ($tree | ForEach-Object { [int] $_.ProcessId })
    $repoPathPattern = [regex]::Escape($RepoRoot)

    $interestingIds =
        @($processTable | Where-Object {
            $_.Name -like "dotnet*" -or
            $_.Name -like "testhost*" -or
            $_.Name -like "vstest*"
        } | ForEach-Object {
            $commandLine = if ($null -eq $_.CommandLine) { "" } else { [string] $_.CommandLine }
            if ($treeIds -contains [int] $_.ProcessId -or $commandLine -match $repoPathPattern) {
                [int] $_.ProcessId
            }
        } | Select-Object -Unique)

    $interesting = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $_.ProcessName -eq "dotnet" -or
        $_.ProcessName -like "testhost*" -or
        $_.ProcessName -like "vstest*"
    })

    $related = @($interesting | Where-Object { $interestingIds -contains $_.Id })
    $other = @($interesting | Where-Object { $interestingIds -notcontains $_.Id })

    $sumCpu = ($related | Measure-Object -Property CPU -Sum).Sum
    if ($null -eq $sumCpu) {
        $sumCpu = 0.0
    }

    $relatedDotnet = @($related | Where-Object { $_.ProcessName -eq "dotnet" }).Count
    $relatedTesthost = @($related | Where-Object { $_.ProcessName -like "testhost*" }).Count
    $otherDotnet = @($other | Where-Object { $_.ProcessName -eq "dotnet" }).Count
    $otherTesthost = @($other | Where-Object { $_.ProcessName -like "testhost*" }).Count

    return @{
        RelatedCpuSeconds = [double] $sumCpu
        RelatedDotnet = $relatedDotnet
        RelatedTesthost = $relatedTesthost
        OtherDotnet = $otherDotnet
        OtherTesthost = $otherTesthost
    }
}

function Update-Phase {
    param(
        [Parameter(Mandatory = $true)]
        [string] $CurrentPhase,
        [AllowEmptyCollection()]
        [string[]] $NewLines,
        [hashtable] $Snapshot
    )

    $phase = $CurrentPhase

    if ($Snapshot -and $Snapshot.RelatedTesthost -gt 0) {
        return "test"
    }

    if ($CurrentPhase -eq "test" -and $Snapshot -and $Snapshot.RelatedDotnet -gt 0) {
        return "test"
    }

    if ($Snapshot -and $Snapshot.RelatedDotnet -gt 1 -and $CurrentPhase -ne "test") {
        $phase = "build"
    }

    foreach ($line in $NewLines) {
        if ($line.Contains("Determining projects to restore") -or $line.Contains("Restored ")) {
            $phase = "restore"
        }
        elseif ($line.Contains("Test run for ") -or $line.Contains("Esecuzione dei test per ") -or $line.Contains("Starting test execution") -or $line.Contains("Passed!") -or $line.Contains("Failed!") -or $line.Contains("Total tests:") -or $line.Contains("Un totale di ") -or $line.Contains("Superato!")) {
            $phase = "test"
        }
        elseif ($line.Contains("Build started") -or $line.Contains("Build succeeded") -or $line.Contains(" -> ")) {
            $phase = "build"
        }
    }

    return $phase
}

function Get-HealthState {
    param(
        [Parameter(Mandatory = $true)]
        [double] $CpuDeltaSeconds,
        [Parameter(Mandatory = $true)]
        [int] $OutputDeltaLines,
        [Parameter(Mandatory = $true)]
        [timespan] $IdleTime,
        [Parameter(Mandatory = $true)]
        [int] $StallSeconds
    )

    if ($OutputDeltaLines -gt 0 -or $CpuDeltaSeconds -ge 0.5) {
        return "active"
    }

    if ($IdleTime.TotalSeconds -ge $StallSeconds) {
        return "possible stall"
    }

    return "waiting"
}

function Format-Duration {
    param(
        [Parameter(Mandatory = $true)]
        [timespan] $Span
    )

    if ($Span.TotalHours -ge 1.0) {
        return [string]::Format("{0:00}:{1:00}:{2:00}", [int] $Span.TotalHours, $Span.Minutes, $Span.Seconds)
    }

    return [string]::Format("{0:00}:{1:00}", $Span.Minutes, $Span.Seconds)
}

function Write-Heartbeat {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Phase,
        [Parameter(Mandatory = $true)]
        [timespan] $Elapsed,
        [Parameter(Mandatory = $true)]
        [timespan] $IdleTime,
        [Parameter(Mandatory = $true)]
        [hashtable] $Snapshot,
        [Parameter(Mandatory = $true)]
        [double] $CpuDeltaSeconds,
        [Parameter(Mandatory = $true)]
        [int] $OutputDeltaLines,
        [Parameter(Mandatory = $true)]
        [int] $WindowSeconds,
        [Parameter(Mandatory = $true)]
        [string] $Health
    )

    $phaseText =
        switch ($Phase) {
            "restore" { "restore/build prep" }
            "build" { "build/testhost prep" }
            "test" { "xUnit suite running" }
            default { "dotnet test bootstrapping" }
        }

    Write-Host (
        "TEST status | phase {0} | elapsed {1} | idle {2} | repo dotnet/testhost {3}/{4} | other dotnet/testhost {5}/{6} | cpu +{7:N1}s/{8}s | output +{9} lines | {10}" -f
        $phaseText,
        (Format-Duration -Span $Elapsed),
        (Format-Duration -Span $IdleTime),
        $Snapshot.RelatedDotnet,
        $Snapshot.RelatedTesthost,
        $Snapshot.OtherDotnet,
        $Snapshot.OtherTesthost,
        $CpuDeltaSeconds,
        $WindowSeconds,
        $OutputDeltaLines,
        $Health
    )
}

function Invoke-TestRun {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ResolvedTargetPath,
        [Parameter(Mandatory = $true)]
        [string] $BuildConfiguration,
        [switch] $SkipBuild,
        [switch] $SkipRestore,
        [int] $TickSeconds,
        [int] $WarnAfterSeconds,
        [string[]] $ExtraArguments
    )

    New-RunDirectory -Path $RunRoot

    $arguments = Get-TestArguments `
        -ResolvedTargetPath $ResolvedTargetPath `
        -BuildConfiguration $BuildConfiguration `
        -SkipBuild:$SkipBuild `
        -SkipRestore:$SkipRestore `
        -ExtraArguments $ExtraArguments

    Write-Host ("Running: dotnet {0}" -f ($arguments -join " "))

    $process = Start-TestProcess `
        -Arguments $arguments `
        -WorkingDirectory $RepoRoot `
        -OutputPath $StdOutPath `
        -ErrorPath $StdErrPath

    $stdoutState = New-StreamState
    $stderrState = New-StreamState
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $lastSignalAt = [DateTime]::UtcNow
    $lastHeartbeatAt = [DateTime]::UtcNow
    $lastCpuSeconds = 0.0
    $phase = "boot"
    $outputSinceHeartbeat = 0

    while (-not $process.HasExited) {
        $snapshot = Get-ActiveProcessSnapshot -RootProcessId $process.Id
        $stdoutLines = @(Write-AppendedLines -Path $StdOutPath -State $stdoutState)
        $stderrLines = @(Write-AppendedLines -Path $StdErrPath -State $stderrState -Prefix "stderr> ")
        $newLineCount = $stdoutLines.Count + $stderrLines.Count
        $outputSinceHeartbeat += $newLineCount

        if ($newLineCount -gt 0) {
            $lastSignalAt = [DateTime]::UtcNow
        }

        $phase = Update-Phase -CurrentPhase $phase -NewLines ($stdoutLines + $stderrLines) -Snapshot $snapshot

        $now = [DateTime]::UtcNow
        if (($now - $lastHeartbeatAt).TotalSeconds -ge $TickSeconds) {
            $cpuDelta = [Math]::Max(0.0, $snapshot.RelatedCpuSeconds - $lastCpuSeconds)
            if ($cpuDelta -ge 0.5) {
                $lastSignalAt = $now
            }

            $idle = $now - $lastSignalAt
            $health = Get-HealthState -CpuDeltaSeconds $cpuDelta -OutputDeltaLines $newLineCount -IdleTime $idle -StallSeconds $WarnAfterSeconds

            Write-Heartbeat `
                -Phase $phase `
                -Elapsed $stopwatch.Elapsed `
                -IdleTime $idle `
                -Snapshot $snapshot `
                -CpuDeltaSeconds $cpuDelta `
                -OutputDeltaLines $outputSinceHeartbeat `
                -WindowSeconds $TickSeconds `
                -Health $health

            $lastCpuSeconds = $snapshot.RelatedCpuSeconds
            $lastHeartbeatAt = $now
            $outputSinceHeartbeat = 0
        }

        Start-Sleep -Milliseconds 500
        $process.Refresh()
    }

    $process.WaitForExit()
    $process.Refresh()
    $stopwatch.Stop()

    $finalStdOut = @(Write-AppendedLines -Path $StdOutPath -State $stdoutState -FlushPartial)
    $finalStdErr = @(Write-AppendedLines -Path $StdErrPath -State $stderrState -Prefix "stderr> " -FlushPartial)

    $finalSnapshot = Get-ActiveProcessSnapshot -RootProcessId $process.Id
    $phase = Update-Phase -CurrentPhase $phase -NewLines ($finalStdOut + $finalStdErr) -Snapshot $finalSnapshot
    $resolvedExitCode = [int] $process.ExitCode
    $finalHealth =
        if ($resolvedExitCode -eq 0) {
            "completed"
        }
        else {
            "failed"
        }

    Write-Heartbeat `
        -Phase $phase `
        -Elapsed $stopwatch.Elapsed `
        -IdleTime ([TimeSpan]::Zero) `
        -Snapshot $finalSnapshot `
        -CpuDeltaSeconds 0.0 `
        -OutputDeltaLines ($finalStdOut.Count + $finalStdErr.Count) `
        -WindowSeconds $TickSeconds `
        -Health $finalHealth

    return $resolvedExitCode
}

try {
    $exitCode = Invoke-TestRun `
        -ResolvedTargetPath $ResolvedTarget `
        -BuildConfiguration $Configuration `
        -SkipBuild:$NoBuild `
        -SkipRestore:$NoRestore `
        -TickSeconds $HeartbeatSeconds `
        -WarnAfterSeconds $StallWarningSeconds `
        -ExtraArguments $AdditionalArguments

    exit $exitCode
}
finally {
    if (Test-Path -LiteralPath $RunRoot) {
        Remove-Item -LiteralPath $RunRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
