# Richtet die geplante Aufgabe "FensterMerker" ein (bei Anmeldung, höchste
# Privilegien — nötig, weil z. B. der Geräte-Manager erhöht läuft und UIPI
# sonst das Verschieben seiner Fenster blockiert) und entfernt die alte
# AutoHotkey-Lösung ("FensterZentrieren").
$ErrorActionPreference = 'Stop'

$exe = "$env:USERPROFILE\Documents\FensterMerker\publish\FensterMerker.exe"

# Alte AutoHotkey-Lösung entfernen
Unregister-ScheduledTask -TaskName 'FensterZentrieren' -Confirm:$false -ErrorAction SilentlyContinue
Get-Process AutoHotkey64 -ErrorAction SilentlyContinue | Stop-Process -Force

$action    = New-ScheduledTaskAction -Execute $exe
$trigger   = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -RunLevel Highest -LogonType Interactive
$settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)

Register-ScheduledTask -TaskName 'FensterMerker' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
Start-ScheduledTask -TaskName 'FensterMerker'

'FERTIG' | Out-File "$env:USERPROFILE\Documents\FensterMerker\setup-ergebnis.txt"
