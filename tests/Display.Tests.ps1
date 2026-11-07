param([string]$OutputDirectory = '.local/display-tests')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$output = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $root $OutputDirectory }
$output = [IO.Path]::GetFullPath($output)
$application = Join-Path $output 'HardwareDeck.exe'
& (Join-Path $root 'Build.ps1') -OutputPath $application

. (Join-Path $root 'scripts/Compiler.ps1')
$testExe = Join-Path $output 'DisplayTests.exe'
Invoke-HardwareDeckCompiler -OutputPath $testExe -Sources (Join-Path $PSScriptRoot 'DisplayTests.cs') -References $application -CompilerOptions '/target:exe'
& $testExe $output (Join-Path $output 'renders')
if ($LASTEXITCODE -ne 0) { throw 'Display validation failed.' }
