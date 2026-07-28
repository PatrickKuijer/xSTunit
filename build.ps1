<#
.SYNOPSIS
    Builds TcXunit and runs its test suite the same way every time.

.DESCRIPTION
    Restores/builds/tests everything dotnet's SDK can handle via
    `dotnet build`/`dotnet test` on TcXunit.sln (TcXunit.Cli, TcXunit.Parser,
    TcXunit.Interpreter, TcXunit.Runner and their *.Tests projects, plus
    TcXunit.Vsix.Tests). TcXunit.sln's own solution configuration already
    excludes TcXunit.Vsix from "Build" -- it's a classic packages.config VSSDK
    project (net472, VS2017 SDK 15.0) that dotnet's MSBuild cannot load; see
    src/TcXunit.Vsix/README.md. This script then makes a best-effort attempt
    to build TcXunit.Vsix separately with a real Visual Studio MSBuild (found
    via vswhere), since that one *can* build it. That step is expected to be
    unavailable or flaky on machines without VS + the VS SDK installed (CI,
    non-Windows, a bare dotnet SDK install) -- it warns and continues rather
    than failing the whole script.

.PARAMETER Configuration
    Build configuration: Debug (default) or Release.

.PARAMETER SkipTests
    Build only; don't run `dotnet test`.

.PARAMETER SkipVsix
    Don't attempt the separate TcXunit.Vsix MSBuild step.

.EXAMPLE
    ./build.ps1
    ./build.ps1 -Configuration Release
    ./build.ps1 -SkipVsix
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$SkipTests,

    [switch]$SkipVsix
)

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$solution = Join-Path $repoRoot 'TcXunit.sln'

function Invoke-Step {
    param(
        [string]$Name,
        [scriptblock]$Action
    )

    Write-Host "==> $Name" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

Invoke-Step "dotnet build ($Configuration)" { dotnet build $solution -c $Configuration }

if (-not $SkipTests) {
    Invoke-Step "dotnet test ($Configuration)" { dotnet test $solution -c $Configuration --no-build }
}

if (-not $SkipVsix) {
    Write-Host "==> TcXunit.Vsix ($Configuration, via Visual Studio MSBuild)" -ForegroundColor Cyan

    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    $msbuild = $null
    if (Test-Path $vswhere) {
        $msbuild = & $vswhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
            Select-Object -First 1
    }

    if (-not $msbuild) {
        Write-Warning "No Visual Studio MSBuild found (vswhere missing or no VS install) -- skipping TcXunit.Vsix. See src/TcXunit.Vsix/README.md for how to build it."
    }
    else {
        $vsixProject = Join-Path $repoRoot 'src\TcXunit.Vsix\TcXunit.Vsix.csproj'
        $packagesConfig = Join-Path $repoRoot 'src\TcXunit.Vsix\packages.config'
        $packagesDir = Join-Path $repoRoot 'packages'
        $vssdkBuildToolsRestored = Test-Path (Join-Path $packagesDir 'Microsoft.VSSDK.BuildTools.15.9.3086')

        $nuget = Get-Command nuget.exe -ErrorAction SilentlyContinue
        if ($nuget) {
            & $nuget.Source restore $packagesConfig -PackagesDirectory $packagesDir -SolutionDirectory $repoRoot
            if ($LASTEXITCODE -ne 0) {
                Write-Warning "nuget restore failed (exit $LASTEXITCODE) -- TcXunit.Vsix build below will likely fail."
            }
        }
        elseif (-not $vssdkBuildToolsRestored) {
            Write-Warning "nuget.exe not found on PATH and packages/ isn't already restored -- TcXunit.Vsix build will likely fail. Install nuget.exe or restore packages via Visual Studio."
        }

        & $msbuild $vsixProject /t:Build /p:Configuration=$Configuration /nologo /v:minimal
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "TcXunit.Vsix build failed (exit $LASTEXITCODE) -- see src/TcXunit.Vsix/README.md for VS SDK prerequisites. Not failing the overall build for this known-flaky step."
        }
        else {
            Write-Host "TcXunit.Vsix built successfully." -ForegroundColor Green
        }
    }
}

Write-Host "Build complete." -ForegroundColor Green
