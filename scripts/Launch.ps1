param([switch]$Sensors, [switch]$Basic, [switch]$CheckOnly, [switch]$Rebuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
try {
    . (Join-Path $PSScriptRoot 'StartupTask.ps1')
    $result = Invoke-DeckLaunch -Root $root -Basic:$Basic -CheckOnly:$CheckOnly -Rebuild:$Rebuild
    if ($CheckOnly) { $result | ConvertTo-Json -Depth 5 }
} catch {
    if ($CheckOnly) { Write-Error $_ }
    else {
        Add-Type -AssemblyName PresentationFramework
        [void][Windows.MessageBox]::Show($_.Exception.Message, 'HardwareDeck startup')
    }
    exit 1
}
