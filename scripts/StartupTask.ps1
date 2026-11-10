# Fixed per-user startup contract. No caller-supplied executable or task arguments.
# A shortcut can inherit PSModulePath from a PowerShell 7 parent while invoking
# Windows PowerShell 5.1. Resolve required OS cmdlets from this runtime explicitly.
foreach ($deckModuleName in @('Microsoft.PowerShell.Management', 'Microsoft.PowerShell.Security', 'Microsoft.PowerShell.Utility', 'CimCmdlets')) {
    $deckModulePath = Join-Path $PSHOME ('Modules\' + $deckModuleName + '\' + $deckModuleName + '.psd1')
    Import-Module -Name $deckModulePath -ErrorAction Stop
}

function Get-DeckUserSid {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try { $identity.User.Value } finally { $identity.Dispose() }
}

function Test-DeckAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    } finally { $identity.Dispose() }
}

function Get-DeckTaskPlan {
    param([string]$UserSid = (Get-DeckUserSid))
    $sid = [Security.Principal.SecurityIdentifier]::new($UserSid)
    $base = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'HardwareDeck'
    $install = Join-Path $base $sid.Value
    [pscustomobject]@{
        UserSid = $sid.Value
        BaseDirectory = $base
        InstallDirectory = $install
        Executable = Join-Path $install 'HardwareDeck.exe'
        TaskFolder = '\HardwareDeck'
        TaskName = 'Dashboard-' + $sid.Value
        TaskPath = '\HardwareDeck\Dashboard-' + $sid.Value
        Marker = 'HardwareDeck managed interactive startup v1'
        PowerShell = Join-Path ([Environment]::GetFolderPath('System')) 'WindowsPowerShell\v1.0\powershell.exe'
        Shortcut = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'HardwareDeck.lnk'
        TaskSddl = 'O:BAG:BAD:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGX;;;' + $sid.Value + ')'
        FolderSddl = 'O:BAG:BAD:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGX;;;BU)'
    }
}

function Get-DeckBundleFiles {
    @('HardwareDeck.exe', 'HardwareDeck.exe.config', 'View.xaml', 'sensor-labels.json',
      'scripts\Launch.ps1', 'scripts\StartupTask.ps1', 'scripts\Manage-Startup.ps1')
}

function Get-DeckBundleSource {
    param([string]$Root, [string]$RelativePath)
    if ((Test-Path -LiteralPath (Join-Path $Root 'src') -PathType Container) -and $RelativePath -notlike 'scripts\*') {
        return Join-Path (Join-Path $Root '.local\startup-package') $RelativePath
    }
    Join-Path $Root $RelativePath
}

function Assert-DeckNoReparse {
    param([string]$Path)
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    while ($null -ne $item) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse points are not allowed: $($item.FullName)" }
        if ($item -is [IO.FileInfo]) { $item = $item.Directory } else { $item = $item.Parent }
    }
}

function Assert-DeckProtectedPath {
    param([string]$Path)
    Assert-DeckNoReparse $Path
    Assert-DeckFileSecurity -Acl (Get-Acl -LiteralPath $Path) -Path $Path
}

function Assert-DeckFileSecurity {
    param($Acl, [string]$Path)
    $raw = [Security.AccessControl.RawSecurityDescriptor]::new($Acl.GetSecurityDescriptorBinaryForm(), 0)
    if ($null -eq $raw.DiscretionaryAcl) { throw "Unrestricted file permissions: $Path" }
    $trusted = @('S-1-5-18', 'S-1-5-32-544')
    if ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -notin $trusted) {
        throw "The installed path is not owned by Administrators or SYSTEM: $Path"
    }
    $writes = [Security.AccessControl.FileSystemRights]::Write -bor
        [Security.AccessControl.FileSystemRights]::Delete -bor
        [Security.AccessControl.FileSystemRights]::DeleteSubdirectoriesAndFiles -bor
        [Security.AccessControl.FileSystemRights]::ChangePermissions -bor
        [Security.AccessControl.FileSystemRights]::TakeOwnership
    foreach ($rule in $acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
        if ($rule.AccessControlType -eq 'Allow' -and $rule.IdentityReference.Value -notin $trusted -and
            ($rule.FileSystemRights -band $writes)) { throw "Non-admin write access to installed path: $Path" }
    }
}

function Test-DeckTaskSecurity {
    param([string]$Sddl, [string]$UserSid)
    $sd = [Security.AccessControl.RawSecurityDescriptor]::new($Sddl)
    if ($sd.Owner.Value -notin @('S-1-5-18','S-1-5-32-544')) { return $false }
    if ($null -eq $sd.DiscretionaryAcl) { return $false }
    if (-not ($sd.ControlFlags -band [Security.AccessControl.ControlFlags]::DiscretionaryAclProtected)) { return $false }
    $userCanRun = $false
    foreach ($ace in $sd.DiscretionaryAcl) {
        if ($ace -isnot [Security.AccessControl.CommonAce] -or $ace.AceQualifier -ne 'AccessAllowed') { return $false }
        $sid = $ace.SecurityIdentifier.Value
        $mask = [long]$ace.AccessMask -band 0xffffffffL
        if ($sid -in @('S-1-5-18','S-1-5-32-544')) { continue }
        # Generic read/execute, or their mapped file rights; never write/delete/WRITE_DAC.
        if ($sid -ne $UserSid -or ($mask -band 0x500d0156L)) { return $false }
        if (($mask -band 0x20000000L) -or ($mask -band 0x20L)) { $userCanRun = $true }
    }
    $userCanRun
}

function Get-DeckTaskDefinitionIssues {
    param($Task, $Plan)
    $d = $Task.Definition
    if ($d.RegistrationInfo.Description -ne $Plan.Marker) { 'Unrecognized task ownership marker' }
    $principalSid = $d.Principal.UserId
    if ($principalSid -notmatch '^S-') {
        try { $principalSid = ([Security.Principal.NTAccount]::new($principalSid)).Translate([Security.Principal.SecurityIdentifier]).Value }
        catch { $principalSid = '' }
    }
    if ($principalSid -ne $Plan.UserSid -or $d.Principal.LogonType -ne 3 -or $d.Principal.RunLevel -ne 1) {
        'Task must use this account, InteractiveToken, and HighestAvailable'
    }
    if ($d.Actions.Count -ne 1) { 'Task must contain exactly one action' }
    else {
        $action = $d.Actions.Item(1)
        if ($action.Type -ne 0 -or $action.Path -ine $Plan.Executable -or
            -not [string]::IsNullOrEmpty($action.Arguments) -or $action.WorkingDirectory -ine $Plan.InstallDirectory) {
            'Task action differs from the fixed HardwareDeck executable'
        }
    }
    if ($d.Triggers.Count -ne 0) { 'The startup task must be on-demand only' }
    if (-not $Task.Enabled -or -not $d.Settings.Enabled -or -not $d.Settings.AllowDemandStart) { 'Task is disabled or cannot run on demand' }
    if ($d.Settings.MultipleInstances -ne 2) { 'Task must ignore duplicate starts' }
    if ($d.Settings.ExecutionTimeLimit -ne 'PT0S' -or $d.Settings.DisallowStartIfOnBatteries -or $d.Settings.StopIfGoingOnBatteries) {
        'Task runtime or power settings differ from the dashboard contract'
    }
    if ($d.Settings.RunOnlyIfIdle -or $d.Settings.RunOnlyIfNetworkAvailable -or $d.Settings.WakeToRun -or $d.Settings.AllowHardTerminate) {
        'Task idle/network/wake/termination settings differ from the dashboard contract'
    }
    if (-not (Test-DeckTaskSecurity -Sddl ($Task.GetSecurityDescriptor(7)) -UserSid $Plan.UserSid)) {
        'Task permissions are not restricted to administrator management and current-user execution'
    }
}

function Connect-DeckScheduler {
    $service = New-Object -ComObject 'Schedule.Service'
    $service.Connect()
    $service
}

function Get-DeckStartupStatus {
    $plan = Get-DeckTaskPlan
    $issues = @()
    $task = $null
    $missing = $false
    try {
        $service = Connect-DeckScheduler
        try { $task = $service.GetFolder($plan.TaskFolder).GetTask($plan.TaskName) }
        catch {
            if (Test-DeckMissingTaskError $_.Exception) { $missing = $true }
            else { throw }
        }
        if ($null -ne $task) {
            $issues += @(Get-DeckTaskDefinitionIssues -Task $task -Plan $plan)
            foreach ($path in @($plan.BaseDirectory, $plan.InstallDirectory, (Join-Path $plan.InstallDirectory 'scripts'))) {
                try { Assert-DeckProtectedPath $path } catch { $issues += $_.Exception.Message }
            }
            foreach ($file in Get-DeckBundleFiles) {
                try { Assert-DeckProtectedPath (Join-Path $plan.InstallDirectory $file) } catch { $issues += $_.Exception.Message }
            }
        }
    } catch { $issues += $_.Exception.Message }
    [pscustomobject]@{
        Configured = $null -ne $task
        Missing = $missing
        Healthy = $null -ne $task -and $issues.Count -eq 0
        TaskPath = $plan.TaskPath
        Executable = $plan.Executable
        UserSid = $plan.UserSid
        State = if ($null -ne $task) { $task.State } else { $null }
        LastTaskResult = if ($null -ne $task) { $task.LastTaskResult } else { $null }
        Issues = $issues
    }
}

function Request-DeckSetup {
    param([string]$Root, [ValidateSet('Setup','Repair','Uninstall')][string]$Action = 'Setup')
    if ($Action -ne 'Uninstall' -and (Test-Path -LiteralPath (Join-Path $Root 'src') -PathType Container)) {
        $labels = Join-Path $Root 'sensor-labels.json'
        if (-not (Test-Path -LiteralPath $labels)) {
            Copy-Item -LiteralPath (Join-Path $Root 'sensor-labels.example.json') -Destination $labels
        }
        & (Join-Path $Root 'Build.ps1') -OutputPath (Join-Path $Root '.local\startup-package\HardwareDeck.exe')
        Copy-Item -LiteralPath $labels -Destination (Join-Path $Root '.local\startup-package\sensor-labels.json') -Force
    }
    $plan = Get-DeckTaskPlan
    $script = Join-Path $Root 'scripts\Manage-Startup.ps1'
    # -File arguments are quoted strings, not an interpolated PowerShell command.
    if ($script.Contains('"')) { throw 'Invalid setup script path.' }
    $arguments = '-NoLogo -NoProfile -ExecutionPolicy Bypass -File "' + $script +
        '" -Action ' + $Action + ' -UserSid "' + $plan.UserSid + '" -Elevated'
    try {
        $process = Start-Process -FilePath $plan.PowerShell -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -PassThru -Wait
    } catch {
        if (Test-DeckCanceledError $_.Exception) { throw 'Administrator approval was canceled. The existing startup configuration was not changed.' }
        throw
    }
    try {
        if ($process.ExitCode -ne 0) { throw "HardwareDeck setup failed (exit $($process.ExitCode)). Run Manage-HardwareDeck.cmd Check for details." }
    } finally { $process.Dispose() }
    # Refresh the checkout copy only after the elevated installer closes it.
    # This write uses the original caller's permissions, not the elevated process.
    if ($Action -ne 'Uninstall' -and (Test-Path -LiteralPath (Join-Path $Root 'src') -PathType Container)) {
        Copy-Item -LiteralPath (Join-Path $Root '.local\startup-package\HardwareDeck.exe') -Destination (Join-Path $Root 'HardwareDeck.exe') -Force
    }
    Update-DeckShortcut -Plan $plan -Root $Root -Remove:($Action -eq 'Uninstall')
}

function Get-DeckShortcutArguments {
    param($Plan)
    '-NoLogo -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "' + (Join-Path $Plan.InstallDirectory 'scripts\Launch.ps1') + '"'
}

function Test-DeckManagedShortcut {
    param($Shortcut, $Plan)
    $Shortcut.TargetPath -ieq $Plan.PowerShell -and $Shortcut.Arguments -ieq (Get-DeckShortcutArguments $Plan)
}

function Update-DeckShortcut {
    param($Plan, [string]$Root, [switch]$Remove)
    # Desktop writes belong to the original launcher, not the elevated installer.
    $shell = New-Object -ComObject WScript.Shell
    if ($Remove) {
        if (Test-Path -LiteralPath $Plan.Shortcut -PathType Leaf) {
            $link = $shell.CreateShortcut($Plan.Shortcut)
            if (Test-DeckManagedShortcut $link $Plan) {
                Remove-Item -LiteralPath $Plan.Shortcut
            }
        }
        return
    }
    $link = $shell.CreateShortcut($Plan.Shortcut)
    $legacyTargets = @($Plan.Executable)
    if ($Root) {
        foreach ($name in @('HardwareDeck.exe','Start-HardwareDeck.cmd','Start-With-Sensors.cmd')) {
            $legacyTargets += Join-Path $Root $name
        }
    }
    $legacy = $link.TargetPath -in $legacyTargets -and [string]::IsNullOrWhiteSpace($link.Arguments)
    if ((Test-Path -LiteralPath $Plan.Shortcut) -and -not $legacy -and -not (Test-DeckManagedShortcut $link $Plan)) {
        throw 'The task is configured, but an unrelated HardwareDeck.lnk already exists. Rename that shortcut before repairing.'
    }
    $link.TargetPath = $Plan.PowerShell
    $link.Arguments = Get-DeckShortcutArguments $Plan
    $link.WorkingDirectory = $Plan.InstallDirectory
    $link.IconLocation = $Plan.Executable + ',0'
    $link.Description = 'HardwareDeck - sensors enabled'
    $link.Save()
}

function Start-DeckTask {
    $status = Get-DeckStartupStatus
    if (-not $status.Healthy) { throw 'Startup task is not valid. Run Manage-HardwareDeck.cmd Repair.' }
    $plan = Get-DeckTaskPlan
    $service = Connect-DeckScheduler
    $task = $service.GetFolder($plan.TaskFolder).GetTask($plan.TaskName)
    if ($task.State -eq 4) { return } # Already running; never stop it for a duplicate click.
    $session = [Diagnostics.Process]::GetCurrentProcess().SessionId
    if ($session -le 0) { throw 'Start HardwareDeck from an interactive Windows login.' }
    $instance = $task.RunEx($null, 4, $session, $null)
    $timer = [Diagnostics.Stopwatch]::StartNew()
    do {
        Start-Sleep -Milliseconds 200
        $instance.Refresh()
        if ($instance.State -eq 4) { return }
        if ($instance.State -ne 2) { break }
    } while ($timer.Elapsed.TotalSeconds -lt 10)
    throw 'The startup task did not reach Running state. Run Manage-HardwareDeck.cmd Check, then Repair if needed.'
}

function Invoke-DeckLaunch {
    param([string]$Root, [switch]$Basic, [switch]$CheckOnly, [switch]$Rebuild)
    $target = Join-Path $Root 'HardwareDeck.exe'
    $buildRequired = $Rebuild -or -not (Test-Path -LiteralPath $target -PathType Leaf)
    if ($CheckOnly) {
        [pscustomobject]@{ BuildRequired = $buildRequired; Target = $target; Startup = if ($Basic) { $null } else { Get-DeckStartupStatus } }
        return
    }
    if ($Basic) {
        if ($buildRequired) { & (Join-Path $Root 'Build.ps1') }
        Start-Process -FilePath $target -WorkingDirectory $Root
        return
    }
    if ($Rebuild) {
        Request-DeckSetup -Root $Root -Action Repair
    } else {
        $status = Get-DeckStartupStatus
        if ($status.Missing) { Request-DeckSetup -Root $Root }
        elseif (-not $status.Healthy) { throw 'Startup task is invalid. Run Manage-HardwareDeck.cmd Check, then Manage-HardwareDeck.cmd Repair (UAC).' }
    }
    $ready = Get-DeckStartupStatus
    if ($ready.Healthy -and $ready.State -ne 4) { Close-DeckExistingDashboard -Root $Root -Plan (Get-DeckTaskPlan) }
    Start-DeckTask
}

function Test-DeckMissingTaskError {
    param([Exception]$Exception)
    while ($null -ne $Exception) {
        if ($Exception.HResult -in @(-2147024894, -2147024893)) { return $true }
        $Exception = $Exception.InnerException
    }
    $false
}

function Test-DeckCanceledError {
    param([Exception]$Exception)
    while ($null -ne $Exception) {
        if (($Exception -is [ComponentModel.Win32Exception] -and $Exception.NativeErrorCode -eq 1223) -or
            $Exception.HResult -eq -2147023673) { return $true }
        $Exception = $Exception.InnerException
    }
    $false
}

function Close-DeckExistingDashboard {
    param([string]$Root, $Plan, [switch]$ValidateOnly)
    $paths = @([IO.Path]::GetFullPath((Join-Path $Root 'HardwareDeck.exe')), $Plan.Executable)
    $session = [Diagnostics.Process]::GetCurrentProcess().SessionId
    $owned = @()
    $processes = @(Get-Process -Name HardwareDeck -ErrorAction SilentlyContinue)
    try {
        foreach ($process in $processes) {
            if ($process.SessionId -ne $session) { continue }
            try { $path = $process.MainModule.FileName } catch { continue }
            if ($path -notin $paths) { continue }
            $native = Get-CimInstance Win32_Process -Filter ('ProcessId = ' + $process.Id)
            if ((Invoke-CimMethod -InputObject $native -MethodName GetOwnerSid).Sid -ne $Plan.UserSid) { continue }
            if ($process.MainWindowHandle -eq [IntPtr]::Zero) {
                throw 'HardwareDeck is starting or a background operation is active. Retry after it finishes.'
            }
            $owned += $process
        }
        if ($ValidateOnly) { return }
        # Check for workers before closing any dashboard window.
        foreach ($process in $owned) {
            if (-not $process.CloseMainWindow() -or -not $process.WaitForExit(10000)) {
                throw 'Close the existing HardwareDeck window before retrying.'
            }
        }
    } finally { foreach ($process in $processes) { $process.Dispose() } }
}
