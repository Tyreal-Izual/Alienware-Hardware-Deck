$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Split-Path -Parent $compiler
$testExe = Join-Path $env:TEMP ('HardwareDeck-tests-' + [guid]::NewGuid().ToString('N') + '.exe')
$argsForCompiler = @('/nologo','/target:exe','/platform:x64','/codepage:65001',('/out:'+$testExe))
foreach ($reference in @('System.dll','System.Core.dll','System.Management.dll','System.ServiceModel.dll','System.Web.Extensions.dll')) { $argsForCompiler += '/reference:'+(Join-Path $framework $reference) }
$argsForCompiler += Join-Path $PSScriptRoot 'src\Sensors.cs'
$argsForCompiler += Join-Path $PSScriptRoot 'src\SensorConfiguration.cs'
$argsForCompiler += Join-Path $PSScriptRoot 'src\CpuTelemetry.cs'
$argsForCompiler += Join-Path $PSScriptRoot 'src\NvidiaReader.cs'
$argsForCompiler += Join-Path $PSScriptRoot 'src\XtuVoltageReader.cs'
$argsForCompiler += Join-Path $PSScriptRoot 'tests\SensorTests.cs'
& $compiler @argsForCompiler
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
try { & $testExe; $result=$LASTEXITCODE } finally { Remove-Item -LiteralPath $testExe -ErrorAction SilentlyContinue }
exit $result
