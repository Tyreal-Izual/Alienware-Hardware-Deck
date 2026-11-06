$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Split-Path -Parent $compiler
$testExe = Join-Path $env:TEMP ('HardwareDeck-tests-' + [guid]::NewGuid().ToString('N') + '.exe')
$argsForCompiler = @('/nologo','/target:exe','/platform:x64','/codepage:65001',('/out:'+$testExe))
foreach ($reference in @('System.dll','System.Core.dll','System.Management.dll','System.ServiceModel.dll','System.Runtime.Serialization.dll','System.Web.Extensions.dll')) { $argsForCompiler += '/reference:'+(Join-Path $framework $reference) }
foreach ($reference in @('UIAutomationClient.dll','UIAutomationTypes.dll','WindowsBase.dll')) { $argsForCompiler += '/reference:'+(Join-Path (Join-Path $framework 'WPF') $reference) }
$argsForCompiler += Join-Path $PSScriptRoot 'src\Sensors.cs'
$argsForCompiler += Join-Path $PSScriptRoot 'src\SensorConfiguration.cs'
$argsForCompiler += Join-Path $PSScriptRoot 'src\CpuTelemetry.cs'
$argsForCompiler += Join-Path $PSScriptRoot 'src\NvidiaReader.cs'
$argsForCompiler += Join-Path $PSScriptRoot 'src\NetworkReader.cs'
$argsForCompiler += Join-Path $PSScriptRoot 'src\XtuVoltageReader.cs'
$argsForCompiler += Join-Path $PSScriptRoot 'src\CpuPresetReader.cs'
$argsForCompiler += Join-Path $PSScriptRoot 'src\PresetControl.cs'
$argsForCompiler += Join-Path $PSScriptRoot 'src\AwccPresetBridge.cs'
$argsForCompiler += Join-Path $PSScriptRoot 'src\WindowPreferences.cs'
$argsForCompiler += Join-Path $PSScriptRoot 'tests\SensorTests.cs'
$argsForCompiler += Join-Path $PSScriptRoot 'tests\WindowPreferencesTests.cs'
$argsForCompiler += Join-Path $PSScriptRoot 'tests\NetworkTests.cs'
$argsForCompiler += Join-Path $PSScriptRoot 'tests\PresetTests.cs'
$argsForCompiler += Join-Path $PSScriptRoot 'tests\PresetLifecycleTests.cs'
& $compiler @argsForCompiler
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
try { & $testExe; $result=$LASTEXITCODE } finally { Remove-Item -LiteralPath $testExe -ErrorAction SilentlyContinue }
exit $result
