# Richtet die geplante Aufgabe "WindowKeeper" ein (bei Anmeldung, höchste
# Privilegien — nötig, weil z. B. der Geräte-Manager erhöht läuft und UIPI
# sonst das Verschieben seiner Fenster blockiert).
$ErrorActionPreference = 'Stop'

$exe = "$env:USERPROFILE\Documents\WindowKeeper\publish\WindowKeeper.exe"

# Aufgaben früherer Namen entfernen
'FensterZentrieren', 'FensterMerker' | ForEach-Object {
    Unregister-ScheduledTask -TaskName $_ -Confirm:$false -ErrorAction SilentlyContinue
}

$action    = New-ScheduledTaskAction -Execute $exe
$trigger   = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -RunLevel Highest -LogonType Interactive
$settings  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)

Register-ScheduledTask -TaskName 'WindowKeeper' -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
Start-ScheduledTask -TaskName 'WindowKeeper'

'FERTIG' | Out-File "$env:USERPROFILE\Documents\WindowKeeper\setup-ergebnis.txt"
