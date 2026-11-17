$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $root 'scripts\Compiler.ps1')
$output = Join-Path $root '.local\gpu-tests'
$target = Join-Path $output 'HardwareDeck.GpuTests.exe'
$sources = @(Get-HardwareDeckSources -Root $root) + (Join-Path $PSScriptRoot 'GpuWorkerTests.cs')
Invoke-HardwareDeckCompiler -OutputPath $target -Sources $sources -CompilerOptions @('/target:exe', '/main:GpuWorkerTests')
$fixture = Join-Path $output 'HardwareDeck.GpuFixture.exe'
Invoke-HardwareDeckCompiler -OutputPath $fixture -Sources $sources -CompilerOptions @('/target:winexe', '/main:GpuWorkerTests')
& $target $output $fixture
exit $LASTEXITCODE
