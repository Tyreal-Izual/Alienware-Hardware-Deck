# Shared compiler configuration for the application, tests, and icon generator.
# Source discovery lives here so adding a production file cannot skip the test build.
function Get-HardwareDeckSources {
    param([Parameter(Mandatory = $true)][string]$Root)
    Get-ChildItem -LiteralPath (Join-Path $Root 'src') -Filter '*.cs' -File |
        Sort-Object Name | ForEach-Object { $_.FullName }
}

function Invoke-HardwareDeckCompiler {
    param(
        [Parameter(Mandatory = $true)][string]$OutputPath,
        [Parameter(Mandatory = $true)][string[]]$Sources,
        [string[]]$References = @(),
        [string[]]$CompilerOptions = @()
    )
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if (-not (Test-Path -LiteralPath $compiler)) {
        throw 'The Windows .NET Framework compiler is required.'
    }
    $framework = Split-Path -Parent $compiler
    $wpf = Join-Path $framework 'WPF'
    $outputFile = [IO.Path]::GetFullPath($OutputPath)
    New-Item -ItemType Directory -Path (Split-Path -Parent $outputFile) -Force | Out-Null
    $arguments = @('/nologo', '/platform:x64', '/codepage:65001', ('/out:' + $outputFile))
    foreach ($reference in @(
        'System.dll', 'System.Core.dll', 'System.Management.dll', 'System.ServiceModel.dll',
        'System.Runtime.Serialization.dll', 'System.Web.Extensions.dll',
        'System.Windows.Forms.dll', 'System.Drawing.dll', 'System.Xaml.dll'
    )) {
        $arguments += '/reference:' + (Join-Path $framework $reference)
    }
    foreach ($reference in @(
        'PresentationFramework.dll', 'PresentationCore.dll', 'WindowsBase.dll',
        'UIAutomationClient.dll', 'UIAutomationTypes.dll'
    )) {
        $arguments += '/reference:' + (Join-Path $wpf $reference)
    }
    foreach ($reference in $References) { $arguments += '/reference:' + $reference }
    $arguments += $CompilerOptions
    $arguments += $Sources
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) { throw "C# compilation failed: $outputFile" }
}
