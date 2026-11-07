$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$toolsDirectory = Join-Path $root '.local\icon-tools'
New-Item -ItemType Directory -Path $toolsDirectory -Force | Out-Null
. (Join-Path $PSScriptRoot 'Compiler.ps1')
$generator = Join-Path $toolsDirectory 'IconGenerator.exe'
Invoke-HardwareDeckCompiler -OutputPath $generator -Sources (Join-Path $PSScriptRoot 'IconGenerator.cs') -References 'System.Xml.dll' -CompilerOptions '/target:exe'
& $generator (Join-Path $root 'assets\HardwareDeck.svg') (Join-Path $root 'assets\HardwareDeck.ico') (Join-Path $toolsDirectory 'preview.png')
if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
Write-Host 'Generated the application icon at 16/20/24/32/40/48/64/128/256 px.'
