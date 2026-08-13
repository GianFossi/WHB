param(
    [ValidateSet("Build", "Test", "Clean", "Rebuild")]
    [string] $Task = "Test",

    [ValidateSet("Debug", "Release")]
    [string] $Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$Solution = Join-Path $PSScriptRoot "WhbDesign.sln"

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
        "tests/Whb.Tests/TestResults"
    )

    foreach ($relativePath in $paths) {
        $path = Join-Path $PSScriptRoot $relativePath
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Recurse -Force
            Write-Host "Removed $relativePath"
        }
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
        dotnet test $Solution --configuration $Configuration --no-build
    }
    "Rebuild" {
        Invoke-Clean
        dotnet restore $Solution
        dotnet build $Solution --configuration $Configuration --no-restore
        dotnet test $Solution --configuration $Configuration --no-build
    }
}
