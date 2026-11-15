$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts\Compiler.ps1')
$testExe = Join-Path $env:TEMP ('HardwareDeck-tests-' + [guid]::NewGuid().ToString('N') + '.exe')
$sources = @(Get-HardwareDeckSources -Root $PSScriptRoot)
foreach ($test in @('SensorTests.cs', 'WindowPreferencesTests.cs', 'NetworkTests.cs', 'PresetTests.cs', 'PresetLifecycleTests.cs', 'StartupInstanceTests.cs', 'CpuLimitsTests.cs')) {
    $sources += Join-Path (Join-Path $PSScriptRoot 'tests') $test
}
try {
    $fixture = Join-Path $PSScriptRoot 'tests\fixtures\r16-startup-controls.json'
    Invoke-HardwareDeckCompiler -OutputPath $testExe -Sources $sources -CompilerOptions @('/target:exe', '/main:SensorTests', ('/resource:' + $fixture + ',HardwareDeck.Tests.FirmwareStartup.json'))
    & $testExe
    $result = $LASTEXITCODE
} finally {
    Remove-Item -LiteralPath $testExe -ErrorAction SilentlyContinue
}
exit $result
