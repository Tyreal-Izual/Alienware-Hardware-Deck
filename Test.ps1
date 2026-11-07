$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'scripts\Compiler.ps1')
$testExe = Join-Path $env:TEMP ('HardwareDeck-tests-' + [guid]::NewGuid().ToString('N') + '.exe')
$sources = @(Get-HardwareDeckSources -Root $PSScriptRoot)
foreach ($test in @('SensorTests.cs', 'WindowPreferencesTests.cs', 'NetworkTests.cs', 'PresetTests.cs', 'PresetLifecycleTests.cs')) {
    $sources += Join-Path (Join-Path $PSScriptRoot 'tests') $test
}
try {
    Invoke-HardwareDeckCompiler -OutputPath $testExe -Sources $sources -CompilerOptions @('/target:exe', '/main:SensorTests')
    & $testExe
    $result = $LASTEXITCODE
} finally {
    Remove-Item -LiteralPath $testExe -ErrorAction SilentlyContinue
}
exit $result
