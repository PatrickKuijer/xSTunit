<#
.SYNOPSIS
    Deploys the built TcXunit.Vsix extension directly into a Visual Studio /
    TwinCAT XAE Shell Extensions folder, bypassing VSIXInstaller.

.DESCRIPTION
    Classic (non-SDK-style) VSSDK projects aren't picked up by `dotnet build`
    and don't auto-install like SDK-style VSIX projects. This copies the
    already-built output (see -Configuration) from this project's own bin
    folder into <ExtensionsRoot>\<Publisher>\<DisplayName>\<Version>, mirroring
    the layout VSIXInstaller itself uses, so XAE Shell picks it up on next
    launch without needing an installer round-trip.

    Only the files actually needed at runtime are copied (skips localized
    VS SDK satellite resource folders, .xml doc comments, and the packaged
    .vsix itself) via robocopy /MIR, so stale files from a previous deploy
    of a since-removed file are cleaned up too.

.PARAMETER Configuration
    Build configuration to deploy from (Debug or Release). Default: Debug.

.PARAMETER TcVersion
    TwinCAT XAE Shell version to deploy into: 4024 (x86 install, the default)
    or 4026 (x64 install). Selects the corresponding default -ExtensionsRoot;
    ignored if -ExtensionsRoot is passed explicitly.

.PARAMETER ExtensionsRoot
    Root Extensions folder to deploy into. Overrides -TcVersion. Default:
    derived from -TcVersion.

.EXAMPLE
    .\deploy.ps1
    Builds nothing; deploys the existing Debug output to the TC 4024 (x86) XAE Shell.

.EXAMPLE
    .\deploy.ps1 -Configuration Release -TcVersion 4026
    Deploys the Release output to the TC 4026 (x64) XAE Shell.

.EXAMPLE
    .\deploy.ps1 -ExtensionsRoot "D:\Custom\Extensions"
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [ValidateSet('4024', '4026')]
    [string]$TcVersion = '4024',

    [string]$ExtensionsRoot
)

if (-not $ExtensionsRoot) {
    $ExtensionsRoot = switch ($TcVersion) {
        '4024' { 'C:\Program Files (x86)\Beckhoff\TcXaeShell\Common7\IDE\Extensions' }
        '4026' { 'C:\Program Files\Beckhoff\TcXaeShell\Common7\IDE\Extensions' }
    }
}

$ErrorActionPreference = 'Stop'

$projectDir = $PSScriptRoot
$sourceDir = Join-Path $projectDir "bin\$Configuration"

if (-not (Test-Path $sourceDir)) {
    throw "Build output not found at '$sourceDir'. Build the project first, e.g.:`n  & `"C:\Program Files (x86)\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\MSBuild.exe`" `"$projectDir\xStunit.Vsix.csproj`" /p:Configuration=$Configuration"
}

# Pull Publisher/DisplayName/Version out of the manifest so the deployed
# path matches the layout VSIXInstaller itself would create.
[xml]$manifest = Get-Content (Join-Path $projectDir 'source.extension.vsixmanifest')
$ns = New-Object System.Xml.XmlNamespaceManager($manifest.NameTable)
$ns.AddNamespace('v', 'http://schemas.microsoft.com/developer/vsx-schema/2011')
$identity = $manifest.SelectSingleNode('//v:PackageManifest/v:Metadata/v:Identity', $ns)
$displayName = $manifest.SelectSingleNode('//v:PackageManifest/v:Metadata/v:DisplayName', $ns).InnerText
$publisher = $identity.Publisher
$version = $identity.Version

$destDir = Join-Path $ExtensionsRoot (Join-Path $publisher (Join-Path $displayName $version))

$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$isElevated = (New-Object Security.Principal.WindowsPrincipal($currentIdentity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isElevated) {
    throw "Deploying to '$ExtensionsRoot' requires an elevated (Run as Administrator) PowerShell session."
}

Write-Host "Deploying $Configuration build:"
Write-Host "  from: $sourceDir"
Write-Host "  to:   $destDir"

# Files/folders actually needed at runtime. Everything else in bin\ (culture
# satellite folders, *.xml doc comments, the packaged .vsix/.pkgdef source
# copy) is build-time clutter this classic csproj happens to copy alongside
# the real output and isn't needed for a directory-deployed extension.
$includeFiles = @(
    'xStunit.Vsix.dll'
    'xStunit.Vsix.pdb'
    'xStunit.Vsix.pkgdef'
    'extension.vsixmanifest'
    'Microsoft.Web.WebView2.Core.dll'
    'Microsoft.Web.WebView2.Wpf.dll'
    'System.Text.Json.dll'
    'System.Text.Encodings.Web.dll'
    'System.Buffers.dll'
    'System.Memory.dll'
    'System.Numerics.Vectors.dll'
    'System.Runtime.CompilerServices.Unsafe.dll'
    'System.Threading.Tasks.Extensions.dll'
    # TcXunit-qwh.4: completes System.Text.Json's net461 dependency closure;
    # previously missing here (and from the csproj), which is what produced
    # the OnAssemblyResolve failure / FileNotFoundException under TcXaeShell.
    'Microsoft.Bcl.AsyncInterfaces.dll'
    'System.ValueTuple.dll'
)

New-Item -ItemType Directory -Path $destDir -Force | Out-Null

foreach ($file in $includeFiles) {
    $src = Join-Path $sourceDir $file
    if (-not (Test-Path $src)) {
        Write-Warning "Skipping missing file: $file"
        continue
    }
    Copy-Item -Path $src -Destination $destDir -Force
}

# Resources\, x86\, and x64\ are mirrored (not just copied) so a file removed
# from the source tree doesn't linger in an old deploy. x86\/x64\ hold the
# two arch-specific WebView2Loader.dll builds (see TcXunit.Vsix.csproj);
# ResultsToolWindowControl picks the right one at runtime by process bitness.
foreach ($folder in @('Resources', 'x86', 'x64')) {
    robocopy (Join-Path $sourceDir $folder) (Join-Path $destDir $folder) /MIR /NFL /NDL /NJH /NJS | Out-Null
    if ($LASTEXITCODE -ge 8) {
        throw "robocopy failed mirroring $folder\ (exit code $LASTEXITCODE)"
    }
}

Write-Host "Deployed to $destDir"
Write-Host "Restart the XAE Shell for the change to take effect."
