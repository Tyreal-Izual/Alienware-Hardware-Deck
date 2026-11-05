param([switch]$Sensors,[switch]$CheckOnly,[switch]$Rebuild)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$target=Join-Path $root 'HardwareDeck.exe'
$buildRequired=$Rebuild -or -not (Test-Path -LiteralPath $target -PathType Leaf)
if($CheckOnly){
    [pscustomobject]@{BuildRequired=$buildRequired;Target=$target}|ConvertTo-Json
    exit 0
}
try {
    if($buildRequired){& (Join-Path $root 'Build.ps1')}
    if($Sensors){Start-Process -FilePath $target -WorkingDirectory $root -Verb RunAs}
    else{Start-Process -FilePath $target -WorkingDirectory $root}
} catch {
    Add-Type -AssemblyName PresentationFramework
    [void][Windows.MessageBox]::Show($_.Exception.Message,'HardwareDeck')
    exit 1
}
