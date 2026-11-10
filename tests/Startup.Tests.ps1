$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $root 'scripts\StartupTask.ps1')
$count = 0
function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:count++
}
function Assert-Rejected([scriptblock]$Operation, [string]$Message) {
    $rejected = $false
    try { & $Operation } catch { $rejected = $true }
    Assert-True $rejected $Message
}
$plan = Get-DeckTaskPlan
function New-FakeTask {
    $action = [pscustomobject]@{ Type=0; Path=$plan.Executable; Arguments=''; WorkingDirectory=$plan.InstallDirectory }
    $actions = [pscustomobject]@{ Count=1; Action=$action }
    $actions | Add-Member ScriptMethod Item { param($index) $this.Action }
    $task = [pscustomobject]@{
        Enabled=$true; State=3; Sddl=$plan.TaskSddl
        Definition=[pscustomobject]@{
            RegistrationInfo=[pscustomobject]@{Description=$plan.Marker}
            Principal=[pscustomobject]@{UserId=$plan.UserSid;LogonType=3;RunLevel=1}
            Actions=$actions; Triggers=[pscustomobject]@{Count=0}
            Settings=[pscustomobject]@{
                Enabled=$true; AllowDemandStart=$true; MultipleInstances=2
                ExecutionTimeLimit='PT0S'; DisallowStartIfOnBatteries=$false; StopIfGoingOnBatteries=$false
                RunOnlyIfIdle=$false; RunOnlyIfNetworkAvailable=$false; WakeToRun=$false; AllowHardTerminate=$false
            }
        }
    }
    $task | Add-Member ScriptMethod GetSecurityDescriptor { param($flags) $this.Sddl }
    $task
}
Assert-True ($plan.Executable.StartsWith([Environment]::GetFolderPath('ProgramFiles') + '\HardwareDeck\')) 'Task executable must be in Program Files.'
Assert-True ($plan.TaskName.EndsWith($plan.UserSid)) 'Task identity must be per-user.'
Assert-True ((Get-DeckBundleFiles) -contains 'View.xaml') 'Privileged XAML must be installed in the protected bundle.'
Assert-True ((Get-DeckBundleFiles) -contains 'HardwareDeck.exe.config') 'CLR configuration must be protected.'
Assert-True ((Get-DeckBundleFiles) -notcontains 'Build.ps1') 'The persistent task must not depend on a checkout build script.'
Assert-True (@(Get-DeckTaskDefinitionIssues (New-FakeTask) $plan).Count -eq 0) 'The exact fixed task contract should validate.'
foreach ($mutation in @(
    {param($t) $t.Definition.Actions.Action.Path='C:\Temp\other.exe'},
    {param($t) $t.Definition.Actions.Action.Arguments='--probe C:\Temp\out.json'},
    {param($t) $t.Definition.Actions.Action.Arguments='$(Arg0)'},
    {param($t) $t.Definition.Actions.Action.WorkingDirectory='C:\Temp'},
    {param($t) $t.Definition.Actions.Count=2},
    {param($t) $t.Definition.Actions.Action.Type=5},
    {param($t) $t.Definition.Principal.LogonType=5},
    {param($t) $t.Definition.Principal.RunLevel=0},
    {param($t) $t.Definition.Principal.UserId='S-1-5-18'},
    {param($t) $t.Definition.Triggers.Count=1},
    {param($t) $t.Enabled=$false},
    {param($t) $t.Definition.Settings.AllowDemandStart=$false},
    {param($t) $t.Definition.Settings.MultipleInstances=0},
    {param($t) $t.Definition.Settings.ExecutionTimeLimit='PT72H'},
    {param($t) $t.Definition.Settings.DisallowStartIfOnBatteries=$true},
    {param($t) $t.Definition.Settings.RunOnlyIfIdle=$true},
    {param($t) $t.Definition.Settings.RunOnlyIfNetworkAvailable=$true},
    {param($t) $t.Definition.Settings.WakeToRun=$true},
    {param($t) $t.Definition.Settings.AllowHardTerminate=$true},
    {param($t) $t.Definition.RegistrationInfo.Description='Unrelated task'}
)) {
    $task=New-FakeTask
    & $mutation $task
    Assert-True (@(Get-DeckTaskDefinitionIssues $task $plan).Count -gt 0) 'A changed task contract must fail closed.'
}
Assert-True (Test-DeckTaskSecurity $plan.TaskSddl $plan.UserSid) 'Current user may read/run the task.'
$writable='O:BAG:BAD:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;' + $plan.UserSid + ')'
Assert-True (-not (Test-DeckTaskSecurity $writable $plan.UserSid)) 'Current user must not edit the privileged task without elevation.'
$shared=$plan.TaskSddl+'(A;;GRGX;;;BU)'
Assert-True (-not (Test-DeckTaskSecurity $shared $plan.UserSid)) 'Other ordinary users must not run this task.'
Assert-True (Test-DeckMissingTaskError ([IO.FileNotFoundException]::new())) 'Missing task detection recognizes HRESULT through exceptions.'
Assert-True (-not (Test-DeckMissingTaskError ([UnauthorizedAccessException]::new()))) 'Access denied must not be treated as first-time setup.'

foreach ($descriptor in @(
    ('O:' + $plan.UserSid + 'G:BAD:P(A;;FA;;;BA)'),
    'O:BAG:BAD:NO_ACCESS_CONTROL',
    'O:BAG:BAD:P(A;;FA;;;BA)(A;;FA;;;BU)'
)) {
    $acl=[Security.AccessControl.DirectorySecurity]::new()
    $acl.SetSecurityDescriptorSddlForm($descriptor)
    Assert-Rejected { Assert-DeckFileSecurity -Acl $acl -Path 'fixture' } 'Unsafe ownership or writable/null DACL must be rejected.'
}
$acl=[Security.AccessControl.DirectorySecurity]::new()
$acl.SetSecurityDescriptorSddlForm('O:BAG:BAD:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FRFX;;;' + $plan.UserSid + ')')
Assert-DeckFileSecurity -Acl $acl -Path 'fixture'
Assert-True $true 'A protected file with current-user read/execute validates.'

$link=[pscustomobject]@{TargetPath=$plan.PowerShell;Arguments=(Get-DeckShortcutArguments $plan)}
Assert-True (Test-DeckManagedShortcut $link $plan) 'The exact managed shortcut is recognized.'
$link.Arguments += ' -Basic'
Assert-True (-not (Test-DeckManagedShortcut $link $plan)) 'A customized shortcut must be preserved on repair/removal.'
$link.Arguments='-NoProfile -File "C:\Temp\other.ps1"'
Assert-True (-not (Test-DeckManagedShortcut $link $plan)) 'Sharing powershell.exe does not make an unrelated shortcut managed.'
$link.Arguments=Get-DeckShortcutArguments $plan
$link.TargetPath='C:\Temp\powershell.exe'
Assert-True (-not (Test-DeckManagedShortcut $link $plan)) 'A different executable cannot be a managed shortcut.'

# Preflight is read-only and must detect a worker before any GUI window is closed.
$script:closeCalls=0
$gui=[pscustomobject]@{SessionId=[Diagnostics.Process]::GetCurrentProcess().SessionId;MainModule=[pscustomobject]@{FileName=$plan.Executable};Id=1;MainWindowHandle=[IntPtr]1}
$gui | Add-Member ScriptMethod CloseMainWindow { $script:closeCalls++; $true }
$gui | Add-Member ScriptMethod WaitForExit { param($timeout) $true }
$gui | Add-Member ScriptMethod Dispose { }
$worker=[pscustomobject]@{SessionId=$gui.SessionId;MainModule=$gui.MainModule;Id=2;MainWindowHandle=[IntPtr]::Zero}
$worker | Add-Member ScriptMethod Dispose { }
$script:fakeProcesses=@($gui)
function Get-Process { param($Name,$ErrorAction) $script:fakeProcesses }
function Get-CimInstance { param($ClassName,$Filter) [pscustomobject]@{ProcessId=1} }
function Invoke-CimMethod { param($InputObject,$MethodName) [pscustomobject]@{Sid=$plan.UserSid} }
try {
    Close-DeckExistingDashboard -Root $root -Plan $plan -ValidateOnly
    Assert-True ($script:closeCalls -eq 0) 'Repair preflight must not close the existing dashboard.'
    $script:fakeProcesses=@($gui,$worker)
    Assert-Rejected { Close-DeckExistingDashboard -Root $root -Plan $plan -ValidateOnly } 'An active worker must block repair preflight.'
    Assert-True ($script:closeCalls -eq 0) 'An active worker leaves the GUI intact.'
} finally {
    Remove-Item function:Get-Process,function:Get-CimInstance,function:Invoke-CimMethod
}

# Exercise launcher decisions without COM registration, GUI launch, or UAC.
$originalRequest = (Get-Item function:Request-DeckSetup).ScriptBlock
$script:setups=0; $script:runs=0; $script:closes=0; $script:basic=0
$script:fakeStatus=[pscustomobject]@{Missing=$true;Healthy=$false;State=$null}
function Get-DeckStartupStatus { $script:fakeStatus }
function Request-DeckSetup {
    param($Root,$Action='Setup')
    $script:setups++;$script:lastAction=$Action
    $script:fakeStatus=[pscustomobject]@{Missing=$false;Healthy=$true;State=3}
}
function Start-DeckTask { $script:runs++;$script:fakeStatus.State=4 }
function Close-DeckExistingDashboard { param($Root,$Plan) $script:closes++ }
function Start-Process { param($FilePath,$WorkingDirectory) $script:basic++ }
Invoke-DeckLaunch -Root $root
Assert-True ($script:setups -eq 1 -and $script:runs -eq 1) 'First managed launch configures once, then starts the task.'
Invoke-DeckLaunch -Root $root
Assert-True ($script:setups -eq 1 -and $script:runs -eq 2) 'Later managed launches do not request setup again.'
Assert-True ($script:closes -eq 1) 'A running task is not closed for duplicate launch.'
$script:fakeStatus=[pscustomobject]@{Missing=$false;Healthy=$false;State=1}
$previous=$script:runs
Assert-Rejected { Invoke-DeckLaunch -Root $root } 'An invalid task must require explicit repair.'
Assert-True ($script:runs -eq $previous -and $script:setups -eq 1) 'Invalid tasks never start or automatically re-prompt for UAC.'
Invoke-DeckLaunch -Root $root -Rebuild
Assert-True ($script:lastAction -eq 'Repair' -and $script:setups -eq 2) 'Explicit rebuild requests the authorized repair/update path.'
$before=$script:setups
$null=Invoke-DeckLaunch -Root $root -CheckOnly
Assert-True ($script:setups -eq $before) 'Check-only performs no setup.'
Invoke-DeckLaunch -Root $root -Basic
Assert-True ($script:basic -eq 1 -and $script:setups -eq $before) 'Explicit basic mode preserves the inherited-permission path.'

Set-Item function:Request-DeckSetup $originalRequest
function Start-Process {
    param($FilePath,$ArgumentList,$Verb,$WindowStyle,[switch]$PassThru,[switch]$Wait)
    if ($script:wrappedCancellation) { throw [InvalidOperationException]::new('Start-Process failed', [ComponentModel.Win32Exception]::new(1223)) }
    throw [ComponentModel.Win32Exception]::new(1223)
}
$canceled=$false
try { Request-DeckSetup -Root ([IO.Path]::GetTempPath()) }
catch { $canceled=$_.Exception.Message -like '*canceled*' }
Assert-True $canceled 'Canceled UAC is reported clearly without running the task.'
$script:wrappedCancellation=$true
$canceled=$false
try { Request-DeckSetup -Root ([IO.Path]::GetTempPath()) }
catch { $canceled=$_.Exception.Message -like '*canceled*' }
Assert-True $canceled 'Wrapped UAC cancellation must be recognized without changing the existing configuration.'
Assert-True (-not (Test-DeckCanceledError ([UnauthorizedAccessException]::new()))) 'Other setup failures must not be mislabeled as cancellation.'
[xml]$manifest=Get-Content -LiteralPath (Join-Path $root 'app.manifest')
Assert-True ($manifest.assembly.trustInfo.security.requestedPrivileges.requestedExecutionLevel.level -eq 'asInvoker') 'Manifest must keep test/diagnostic entry points asInvoker.'
$tokens=$null; $parseErrors=$null
$management=[Management.Automation.Language.Parser]::ParseFile((Join-Path $root 'scripts\Manage-Startup.ps1'),[ref]$tokens,[ref]$parseErrors)
$actionWrites=@($management.FindAll({param($node) $node -is [Management.Automation.Language.AssignmentStatementAst] -and $node.Left -is [Management.Automation.Language.VariableExpressionAst] -and $node.Left.VariablePath.UserPath -ieq 'Action'},$true))
Assert-True ($parseErrors.Count -eq 0 -and $actionWrites.Count -eq 0) 'Registration must not shadow the case-insensitive Action parameter.'

# Reproduce a shortcut inheriting an incompatible module search path. No task is run.
$probeRoot=Join-Path ([IO.Path]::GetTempPath()) ('HardwareDeck startup '+[guid]::NewGuid().ToString('N'))
$originalModulePath=$env:PSModulePath
try {
    $fakeModule=Join-Path $probeRoot 'modules\Microsoft.PowerShell.Security'
    New-Item -ItemType Directory -Path $fakeModule -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $fakeModule 'Microsoft.PowerShell.Security.psd1') -Value "@{ModuleVersion='99.0';PowerShellVersion='99.0';FunctionsToExport=@('Get-Acl')}" -Encoding UTF8
    $probe=Join-Path $probeRoot 'Probe.ps1'
    @'
param([string]$Library)
$ErrorActionPreference='Stop'
. $Library
if (-not (Get-Command Get-Acl).Module.Path.StartsWith((Join-Path $PSHOME 'Modules'),[StringComparison]::OrdinalIgnoreCase)) { throw 'Wrong security module loaded.' }
'@ | Set-Content -LiteralPath $probe -Encoding UTF8
    $env:PSModulePath=(Join-Path $probeRoot 'modules')+';'+$originalModulePath
    $child=Microsoft.PowerShell.Management\Start-Process -FilePath $plan.PowerShell -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File "'+$probe+'" -Library "'+(Join-Path $root 'scripts\StartupTask.ps1')+'"') -WindowStyle Hidden -PassThru -Wait
    try { Assert-True ($child.ExitCode -eq 0) 'Shortcut launch must use runtime-matching OS modules despite inherited PSModulePath.' }
    finally { $child.Dispose() }
} finally {
    $env:PSModulePath=$originalModulePath
    $resolved=[IO.Path]::GetFullPath($probeRoot)
    if (-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'HardwareDeck startup *') { throw 'Unsafe test cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
Write-Host "$count startup-task contract and launcher checks passed (no task registration)."
