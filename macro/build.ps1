param(
    [ValidateSet("Build", "Test", "Clean", "Rebuild")]
    [string] $Task = "Test",

    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$Solution = Join-Path $RepoRoot "WhbDesign.sln"
$TestScript = Join-Path $PSScriptRoot "test.ps1"

function Remove-RepoPath {
    param(
        [Parameter(Mandatory = $true)]
        [string] $RelativePath
    )

    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $RelativePath))
    if (-not $fullPath.StartsWith($RepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove path outside repository root: $RelativePath"
    }

    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
        Write-Host "Removed $RelativePath"
    }
}

function Invoke-Clean {
    dotnet clean $Solution --configuration Debug
    dotnet clean $Solution --configuration Release

    $paths = @(
        "publish",
        "src/Whb.Core/bin",
        "src/Whb.Core/obj",
        "src/Whb.Cli/bin",
        "src/Whb.Cli/obj",
        "tests/Whb.Tests/bin",
        "tests/Whb.Tests/obj",
        "tests/Whb.Tests/TestResults",
        "results",
        "results_check",
        "results_pds_check",
        "results_probe",
        "results_report_option_probe",
        "results_precision_probe",
        "results_summary_probe",
        "risultati",
        "risultati_check",
        "risultati_pds_check",
        "tmp",
        "temp",
        "logs",
        "artifacts/packages",
        "tmp_run_stdout.txt",
        "tmp_run_stderr.txt"
    )

    foreach ($relativePath in $paths) {
        Remove-RepoPath -RelativePath $relativePath
    }
}

switch ($Task) {
    "Clean" {
        Invoke-Clean
    }
    "Build" {
        dotnet restore $Solution
        dotnet build $Solution --configuration $Configuration --no-restore
    }
    "Test" {
        dotnet restore $Solution
        dotnet build $Solution --configuration $Configuration --no-restore
        & $TestScript -Target $Solution -Configuration $Configuration -NoBuild -NoRestore
    }
    "Rebuild" {
        Invoke-Clean
        dotnet restore $Solution
        dotnet build $Solution --configuration $Configuration --no-restore
        & $TestScript -Target $Solution -Configuration $Configuration -NoBuild -NoRestore
    }
}
