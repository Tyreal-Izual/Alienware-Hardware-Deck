param(
    [ValidateSet('Setup','Check','Repair','Uninstall')][string]$Action = 'Check',
    [string]$UserSid,
    [switch]$Elevated
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'StartupTask.ps1')

function Set-DeckDirectorySecurity {
    param([string]$Path, [string]$ReaderSid)
    Assert-DeckNoReparse $Path
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetAccessRuleProtection($true, $false)
    $acl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
    foreach ($sid in @('S-1-5-18','S-1-5-32-544')) {
        $rule = [Security.AccessControl.FileSystemAccessRule]::new(
            [Security.Principal.SecurityIdentifier]::new($sid), 'FullControl',
            'ContainerInherit, ObjectInherit', 'None', 'Allow')
        $acl.AddAccessRule($rule)
    }
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
        [Security.Principal.SecurityIdentifier]::new($ReaderSid), 'ReadAndExecute',
        'ContainerInherit, ObjectInherit', 'None', 'Allow'))
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Set-DeckFileSecurity {
    param([string]$Path)
    Assert-DeckNoReparse $Path
    $acl = [Security.AccessControl.FileSecurity]::new()
    $acl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
    $acl.SetAccessRuleProtection($false, $false)
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Assert-DeckExistingTask {
    param($Task, $Plan)
    if ($null -ne $Task -and $Task.Definition.RegistrationInfo.Description -ne $Plan.Marker) {
        throw 'A different task already uses this name. It was not modified.'
    }
}

try {
    if ($Action -eq 'Check') {
        $status = Get-DeckStartupStatus
        $status | ConvertTo-Json -Depth 5
        if (-not $status.Healthy) { exit 1 }
        exit 0
    }
    if (-not $Elevated) {
        Request-DeckSetup -Root $root -Action $Action
        exit 0
    }
    if (-not (Test-DeckAdministrator)) { throw 'Administrator access is required for task management.' }
    $currentSid = Get-DeckUserSid
    if ($UserSid -and $UserSid -ne $currentSid) {
        throw 'Use the same Windows account for setup and daily launch. A different administrator account cannot configure this task.'
    }
    $plan = Get-DeckTaskPlan -UserSid $currentSid
    $service = Connect-DeckScheduler
    $folder = $null
    $existing = $null
    try { $folder = $service.GetFolder($plan.TaskFolder) }
    catch { if (-not (Test-DeckMissingTaskError $_.Exception)) { throw } }
    if ($null -ne $folder) {
        try { $existing = $folder.GetTask($plan.TaskName) }
        catch { if (-not (Test-DeckMissingTaskError $_.Exception)) { throw } }
    }
    Assert-DeckExistingTask $existing $plan

    if ($Action -eq 'Uninstall') {
        if ($null -ne $existing) { $folder.DeleteTask($plan.TaskName, 0) }
        Write-Output 'HardwareDeck task removed. Application files and window preferences were retained.'
        exit 0
    }

    # The UAC-approved checkout is copied once; the persistent task never executes it.
    foreach ($file in Get-DeckBundleFiles) { Assert-DeckNoReparse (Get-DeckBundleSource -Root $root -RelativePath $file) }
    foreach ($directory in @($plan.BaseDirectory, $plan.InstallDirectory)) {
        if (Test-Path -LiteralPath $directory) { Assert-DeckProtectedPath $directory }
    }
    Close-DeckExistingDashboard -Root $root -Plan $plan -ValidateOnly
    if ($null -ne $existing) { $existing.Enabled = $false }
    Close-DeckExistingDashboard -Root $root -Plan $plan

    New-Item -ItemType Directory -Path $plan.BaseDirectory -Force | Out-Null
    Set-DeckDirectorySecurity -Path $plan.BaseDirectory -ReaderSid 'S-1-5-32-545'
    New-Item -ItemType Directory -Path $plan.InstallDirectory -Force | Out-Null
    Set-DeckDirectorySecurity -Path $plan.InstallDirectory -ReaderSid $plan.UserSid
    $scripts = Join-Path $plan.InstallDirectory 'scripts'
    if (Test-Path -LiteralPath $scripts) { Assert-DeckProtectedPath $scripts }
    New-Item -ItemType Directory -Path $scripts -Force | Out-Null
    Set-DeckDirectorySecurity -Path $scripts -ReaderSid $plan.UserSid

    foreach ($file in Get-DeckBundleFiles) {
        $source = Get-DeckBundleSource -Root $root -RelativePath $file
        $destination = Join-Path $plan.InstallDirectory $file
        if (Test-Path -LiteralPath $destination) { Assert-DeckProtectedPath $destination }
        if ([IO.Path]::GetFullPath($source) -ine [IO.Path]::GetFullPath($destination)) {
            Copy-Item -LiteralPath $source -Destination $destination -Force
        }
        Set-DeckFileSecurity $destination
        Assert-DeckProtectedPath $destination
    }

    if ($null -eq $folder) { $folder = $service.GetFolder('\').CreateFolder('HardwareDeck', $plan.FolderSddl) }
    else { $folder.SetSecurityDescriptor($plan.FolderSddl, 0) }
    $definition = $service.NewTask(0)
    $definition.RegistrationInfo.Description = $plan.Marker
    $definition.RegistrationInfo.Author = 'HardwareDeck'
    $definition.Principal.UserId = $plan.UserSid
    $definition.Principal.LogonType = 3 # InteractiveToken: logged-on user only; no stored password.
    $definition.Principal.RunLevel = 1 # HighestAvailable.
    $definition.Settings.Enabled = $true
    $definition.Settings.AllowDemandStart = $true
    $definition.Settings.MultipleInstances = 2 # IgnoreNew.
    $definition.Settings.ExecutionTimeLimit = 'PT0S'
    $definition.Settings.DisallowStartIfOnBatteries = $false
    $definition.Settings.StopIfGoingOnBatteries = $false
    $definition.Settings.AllowHardTerminate = $false
    $definition.Settings.WakeToRun = $false
    $definition.Settings.RunOnlyIfIdle = $false
    $definition.Settings.RunOnlyIfNetworkAvailable = $false
    $execAction = $definition.Actions.Create(0)
    $execAction.Path = $plan.Executable
    $execAction.Arguments = ''
    $execAction.WorkingDirectory = $plan.InstallDirectory
    # Do not add an unrestricted principal ACE; read/execute is explicit in the DACL.
    $registered = $folder.RegisterTaskDefinition($plan.TaskName, $definition, 22, $plan.UserSid, $null, 3, $plan.TaskSddl)
    $status = Get-DeckStartupStatus
    if (-not $status.Healthy) {
        $registered.Enabled = $false
        throw ('Installed task failed validation: ' + ($status.Issues -join '; '))
    }

    Write-Output 'HardwareDeck startup configured. Launch through the shortcut or Start-HardwareDeck.cmd.'
} catch {
    Add-Type -AssemblyName PresentationFramework
    [void][Windows.MessageBox]::Show($_.Exception.Message, 'HardwareDeck setup')
    exit 1
}
