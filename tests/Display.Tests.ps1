param([string]$OutputDirectory = '.local/display-tests')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $root $OutputDirectory }
$output = [IO.Path]::GetFullPath($output)
$application = Join-Path $output 'HardwareDeck.exe'
& (Join-Path $root 'Build.ps1') -OutputPath $application

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Split-Path -Parent $compiler
$testExe = Join-Path $output 'DisplayTests.exe'
$compilerArgs = @('/nologo', '/target:exe', '/platform:x64', '/codepage:65001', ('/out:' + $testExe), ('/reference:' + $application))
foreach ($reference in @('System.dll', 'System.Core.dll', 'System.Xaml.dll')) {
    $compilerArgs += '/reference:' + (Join-Path $framework $reference)
}
foreach ($reference in @('PresentationFramework.dll', 'PresentationCore.dll', 'WindowsBase.dll')) {
    $compilerArgs += '/reference:' + (Join-Path (Join-Path $framework 'WPF') $reference)
}
$compilerArgs += Join-Path $PSScriptRoot 'DisplayTests.cs'
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Display test compilation failed.' }
& $testExe $output (Join-Path $output 'renders')
if ($LASTEXITCODE -ne 0) { throw 'Display validation failed.' }
