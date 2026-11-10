# Elevated daily startup

**English** | [简体中文](STARTUP.zh-CN.md)

## First configuration

Run **Start-HardwareDeck.cmd** from your normal Windows login. The launcher builds a
staged package, requests one UAC approval, and registers a dedicated on-demand task.
Setup uses the same Windows account; that account must be an administrator. Supplying
credentials for a different administrator is rejected rather than switching profiles.

The approved application and its view/configuration are installed under
`%ProgramFiles%\HardwareDeck\<SID>`. Administrators and SYSTEM own and manage the
installation; the current user has read/execute access. The task never points to the
writable checkout, a command interpreter, or a caller-selected executable.

An existing dashboard is closed normally during setup so its window preferences are
saved. Active background operations are checked before disabling the existing task
and must finish first. The launcher creates a
**HardwareDeck** desktop shortcut and starts the task after successful validation.
Desktop files and the checkout executable are updated by the original launcher,
outside the elevated installer.

## Daily launch

Use the desktop shortcut, **Start-HardwareDeck.cmd**, or **Start-With-Sensors.cmd**.
After configuration these entries run the same fixed task without another UAC prompt;
the dashboard starts with sensors enabled. The Sensors script remains a compatibility
alias. Registration has no automatic logon/boot trigger: the task runs on demand.

The task uses the current account, `InteractiveToken` and `HighestAvailable`, and is
started in the caller's interactive session. It does not store a password or run as
SYSTEM. The action has one fixed executable, an empty argument string, and a protected
working directory. No `$(Arg0)` substitution or arbitrary diagnostic arguments are accepted.

The scheduler uses `IgnoreNew` for duplicate starts. A per-user, session-local mutex
also prevents duplicate dashboard windows across direct and managed launches. Probe,
preview and preset-worker processes keep their separate entry points. A normal window
can finish closing during its existing Enable sensors elevation handoff.

Monitor identity, size, fullscreen/maximized state and pinning still use
`%LOCALAPPDATA%\HardwareDeck\window-state.json` for the same user. Existing CPU/OC/cooling
guards and hardware readback remain in place.

## Check, repair, update and remove

Run these commands from a normal terminal in the checkout, or double-click
**Manage-HardwareDeck.cmd** to display the check result:

```text
Manage-HardwareDeck.cmd Check
Manage-HardwareDeck.cmd Repair
Manage-HardwareDeck.cmd Uninstall
```

- **Check** is read-only. It reports task identity, state, last result, fixed action,
  installed location and validation issues. A healthy task must have the expected
  account, interactive/highest flags, duplicate policy, action and permissions.
- **Repair** requests UAC again. From the checkout it rebuilds a staged package,
  closes the old dashboard normally, refreshes the protected installation and sensor
  labels, restores the task definition, and recreates the shortcut. Source updates
  require this explicit step; daily launch does not silently elevate new checkout code.
- **Uninstall** requests UAC and removes only this user's recognized task and managed
  shortcut. Application files and window preferences are retained. No unrelated task,
  Dell service, or other user's registration is removed. Modified or unrelated
  same-name shortcuts are preserved rather than overwritten or deleted.

The task name is `\HardwareDeck\Dashboard-<SID>`. Repair/removal verifies the management
marker before changing an existing task. Unexpected ownership, writable installed code,
or reparse points cause failure instead of a permissive fallback.

Edit the checkout's Git-ignored `sensor-labels.json`, then use **Repair** to deploy it.
The installed copy is protected. For developer-only inherited-permission startup use:

```text
Start-HardwareDeck.cmd -Basic
```

## Failure and EXE behavior

Canceling UAC stops configuration; it does not register a task or fall back to an
unverified elevated launch. A disabled, altered, missing-file, or otherwise invalid
existing task is not run. The launcher asks for explicit **Check**/**Repair** instead
of repeatedly showing UAC. A genuinely missing registration uses first-time setup.
An interrupted repair can leave the task disabled until Repair succeeds.

The manifest deliberately remains `asInvoker`. Directly double-clicking an EXE inherits
the caller's permissions and can show **Enable sensors**; that action still uses UAC.
It is not the repeat-confirmation-free entry point. Changing the manifest to
`requireAdministrator` alone would not provide this task-based launch behavior and
would unnecessarily elevate tests/diagnostics.

## Validation and platform references

Offline checks cover setup/repeated-launch decisions, canceled UAC, invalid task
actions/principals/settings, task/file permissions, and the single-instance handoff.
They do not register tasks or alter hardware modes. Live acceptance is recorded in
[VERIFICATION.md](VERIFICATION.md).

Windows mechanisms: [task security and run levels](https://learn.microsoft.com/en-us/windows/win32/taskschd/security-contexts-for-running-tasks),
[interactive registration](https://learn.microsoft.com/en-us/windows/win32/taskschd/taskfolder-registertaskdefinition),
[session-specific RunEx](https://learn.microsoft.com/en-us/windows/win32/api/taskschd/nf-taskschd-iregisteredtask-runex),
[duplicate-instance policy](https://learn.microsoft.com/en-us/windows/win32/taskschd/tasksettings-multipleinstances).
