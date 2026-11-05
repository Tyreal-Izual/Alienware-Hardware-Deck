$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$toolsDirectory = Join-Path $root '.local\icon-tools'
New-Item -ItemType Directory -Path $toolsDirectory -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$generator = Join-Path $toolsDirectory 'IconGenerator.exe'
& $compiler /nologo /target:exe /platform:x64 /reference:System.Drawing.dll /reference:System.Xml.dll ('/out:' + $generator) (Join-Path $PSScriptRoot 'IconGenerator.cs')
if ($LASTEXITCODE -ne 0) { throw 'Icon generator compilation failed.' }
& $generator (Join-Path $root 'assets\HardwareDeck.svg') (Join-Path $root 'assets\HardwareDeck.ico') (Join-Path $toolsDirectory 'preview.png')
if ($LASTEXITCODE -ne 0) { throw 'Icon generation failed.' }
Write-Host 'Generated the application icon at 16/20/24/32/40/48/64/128/256 px.'
