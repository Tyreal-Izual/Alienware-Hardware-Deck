param([string]$OutputPath = 'HardwareDeck.exe')
$ErrorActionPreference = 'Stop'
$outputFile = if ([IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $PSScriptRoot $OutputPath }
$outputFile = [IO.Path]::GetFullPath($outputFile)
$outputDirectory = Split-Path -Parent $outputFile
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
. (Join-Path $PSScriptRoot 'scripts\Compiler.ps1')
$arguments = @('/target:winexe', '/optimize+')
$arguments += '/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')
$icon = Join-Path $PSScriptRoot 'assets\HardwareDeck.ico'
if (-not (Test-Path -LiteralPath $icon)) { throw 'Missing application icon. Run scripts/Build-Icon.ps1.' }
$arguments += '/win32icon:' + $icon
$arguments += '/resource:' + $icon + ',HardwareDeck.AppIcon.ico'
Invoke-HardwareDeckCompiler -OutputPath $outputFile -Sources @(Get-HardwareDeckSources -Root $PSScriptRoot) -CompilerOptions $arguments

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
