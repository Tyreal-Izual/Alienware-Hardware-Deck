param([string]$OutputPath = 'HardwareDeck.exe')
$ErrorActionPreference = 'Stop'
$outputFile = if ([IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $PSScriptRoot $OutputPath }
$outputFile = [IO.Path]::GetFullPath($outputFile)
$outputDirectory = Split-Path -Parent $outputFile
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The Windows .NET Framework compiler is required.' }
$framework = Split-Path -Parent $compiler
$wpf = Join-Path $framework 'WPF'
$arguments = @('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001',('/out:' + $outputFile))
foreach ($reference in @('System.dll','System.Core.dll','System.Management.dll','System.ServiceModel.dll','System.Runtime.Serialization.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Drawing.dll','System.Xaml.dll')) {
    $arguments += '/reference:' + (Join-Path $framework $reference)
}
foreach ($reference in @('PresentationFramework.dll','PresentationCore.dll','WindowsBase.dll','UIAutomationClient.dll','UIAutomationTypes.dll')) {
    $arguments += '/reference:' + (Join-Path $wpf $reference)
}
$arguments += '/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')
$icon = Join-Path $PSScriptRoot 'assets\HardwareDeck.ico'
if (-not (Test-Path -LiteralPath $icon)) { throw 'Missing application icon. Run scripts/Build-Icon.ps1.' }
$arguments += '/win32icon:' + $icon
$arguments += '/resource:' + $icon + ',HardwareDeck.AppIcon.ico'
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File | Sort-Object Name | ForEach-Object {
    $arguments += $_.FullName
}
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed. Close any running HardwareDeck window before replacing its executable.' }

foreach ($asset in @('View.xaml','HardwareDeck.exe.config')) {
    $source = Join-Path $PSScriptRoot $asset
    $destination = Join-Path $outputDirectory $asset
    if ([IO.Path]::GetFullPath($source) -ne [IO.Path]::GetFullPath($destination)) {
        Copy-Item -LiteralPath $source -Destination $destination -Force
    }
}
$config = Join-Path $outputDirectory 'sensor-labels.json'
if (-not (Test-Path -LiteralPath $config)) {
    $localConfig = Join-Path $PSScriptRoot 'sensor-labels.json'
    $sourceConfig = if (Test-Path -LiteralPath $localConfig) { $localConfig } else { Join-Path $PSScriptRoot 'sensor-labels.example.json' }
    Copy-Item -LiteralPath $sourceConfig -Destination $config
}
Write-Host "Built $outputFile"
