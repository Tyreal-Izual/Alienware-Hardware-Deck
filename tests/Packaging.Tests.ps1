$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$workspace=Join-Path ([IO.Path]::GetTempPath()) ('HardwareDeck packaging '+[guid]::NewGuid().ToString('N'))
$count=0
function Assert-True([bool]$condition,[string]$message){
    if(-not $condition){throw $message}
    $script:count++
}
# Intercept only the launcher's final GUI/elevation action; builds still run for real.
function Start-Process {
    param([string]$FilePath,[string]$WorkingDirectory,[string]$Verb)
    $global:HardwareDeckPackagingLaunch=[pscustomobject]@{File=$FilePath;Directory=$WorkingDirectory;Verb=$Verb}
}
try {
    New-Item -ItemType Directory -Path $workspace -Force | Out-Null
    foreach($folder in @('src','scripts')){Copy-Item -LiteralPath (Join-Path $root $folder) -Destination $workspace -Recurse}
    foreach($file in @('Build.ps1','View.xaml','HardwareDeck.exe.config','app.manifest','sensor-labels.example.json')){
        Copy-Item -LiteralPath (Join-Path $root $file) -Destination $workspace
    }
    $launcher=Join-Path $workspace 'scripts\Launch.ps1'
    $target=Join-Path $workspace 'HardwareDeck.exe'
    $before=& $launcher -CheckOnly | ConvertFrom-Json
    Assert-True $before.BuildRequired 'A fresh checkout must report that a build is required.'

    & $launcher
    Assert-True (Test-Path -LiteralPath $target) 'First launch must build the executable.'
    Assert-True ($global:HardwareDeckPackagingLaunch.File -eq $target) 'Launcher must target the checkout executable.'
    Assert-True ([string]::IsNullOrEmpty($global:HardwareDeckPackagingLaunch.Verb)) 'Normal launch must not request elevation.'
    Assert-True (Test-Path -LiteralPath (Join-Path $workspace 'sensor-labels.json')) 'First build must create the local configuration.'

    $pending=Join-Path $workspace '.local\pending-update'
    New-Item -ItemType Directory -Path $pending -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $pending 'HardwareDeck.exe'),'obsolete development artifact')
    $hash=(Get-FileHash -LiteralPath $target).Hash
    & $launcher -Sensors
    Assert-True ($global:HardwareDeckPackagingLaunch.Verb -eq 'RunAs') 'Sensor launch must explicitly request elevation.'
    Assert-True ((Get-FileHash -LiteralPath $target).Hash -eq $hash) 'Old pending binaries must never replace a fresh build.'

    $bundle=Join-Path $workspace 'portable bundle\HardwareDeck.exe'
    & (Join-Path $workspace 'Build.ps1') -OutputPath $bundle
    foreach($asset in @('HardwareDeck.exe','View.xaml','HardwareDeck.exe.config','sensor-labels.json')){
        Assert-True (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $bundle) $asset)) "Missing portable asset: $asset"
    }
    $config=Join-Path $workspace 'sensor-labels.json'
    [IO.File]::WriteAllText($config,'{"cpuTemperatureSensor":"awcc.temp.5","labels":{}}')
    $configHash=(Get-FileHash -LiteralPath $config).Hash
    & $launcher -Rebuild
    Assert-True ((Get-FileHash -LiteralPath $config).Hash -eq $configHash) 'Rebuild must preserve local sensor preferences.'

    foreach($arguments in @('--unknown','--test-modes ignored.json')){
        $process=Microsoft.PowerShell.Management\Start-Process -FilePath $target -ArgumentList $arguments -WindowStyle Hidden -PassThru
        try {
            if(-not $process.WaitForExit(10000)){$process.Kill();$process.WaitForExit();throw "Invalid arguments opened a persistent process: $arguments"}
            Assert-True ($process.ExitCode -eq 2) "Invalid/removed command must fail without hardware actions: $arguments"
        } finally {$process.Dispose()}
    }
    Write-Host "$count clean-checkout and packaging checks passed."
} finally {
    Remove-Variable -Name HardwareDeckPackagingLaunch -Scope Global -ErrorAction SilentlyContinue
    $resolved=[IO.Path]::GetFullPath($workspace)
    $temporaryRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if(-not $resolved.StartsWith($temporaryRoot,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'HardwareDeck packaging *'){
        throw 'Refusing cleanup outside the dedicated test directory.'
    }
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
