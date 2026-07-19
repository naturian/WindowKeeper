# Registers the "WindowKeeper" scheduled task (run at logon, highest
# privileges). Elevated rights are required because e.g. Device Manager
# auto-elevates, and UIPI would otherwise block moving its windows.
$ErrorActionPreference = 'Stop'

$exe = "$env:USERPROFILE\Documents\WindowKeeper\publish\WindowKeeper.exe"

$action    = New-ScheduledTaskAction -Execute $exe
$trigger   = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -RunLevel Highest -LogonType Interactive
$settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)

Register-ScheduledTask -TaskName 'WindowKeeper' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
Start-ScheduledTask -TaskName 'WindowKeeper'

'DONE' | Out-File "$env:USERPROFILE\Documents\WindowKeeper\setup-result.txt"
