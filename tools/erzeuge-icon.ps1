Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$zielDatei = "$env:USERPROFILE\Documents\FensterMerker\icon.ico"
$groessen = 16, 20, 24, 32, 48, 64, 128, 256
$pngs = @()

foreach ($sz in $groessen) {
    $bmp = New-Object System.Drawing.Bitmap($sz, $sz, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $s = $sz / 16.0

    # Fensterrahmen mit Titelleiste (weiß, wie Windows-11-Tray-Symbole)
    $stift = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [float](1.4 * $s))
    $stift.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawRectangle($stift, [float](1.5 * $s), [float](2.5 * $s), [float](13.0 * $s), [float](11.0 * $s))
    $g.DrawLine($stift, [float](1.5 * $s), [float](5.5 * $s), [float](14.5 * $s), [float](5.5 * $s))

    # Positionspunkt in Akzentfarbe in der Mitte der Fensterfläche
    $pinsel = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 76, 194, 255))
    $radius = 2.4 * $s
    $g.FillEllipse($pinsel, [float](8.0 * $s - $radius), [float](9.5 * $s - $radius), [float](2 * $radius), [float](2 * $radius))

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $pngs += ,@($sz, $ms.ToArray())
    $ms.Dispose()
}

# ICO-Container schreiben (PNG-Einträge, seit Vista unterstützt)
$fs = [System.IO.File]::Create($zielDatei)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0)                 # reserviert
$bw.Write([uint16]1)                 # Typ: Icon
$bw.Write([uint16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($eintrag in $pngs) {
    $sz = $eintrag[0]; $daten = $eintrag[1]
    $bw.Write([byte]($sz -band 0xFF))  # 256 -> 0
    $bw.Write([byte]($sz -band 0xFF))
    $bw.Write([byte]0)                 # Farbanzahl
    $bw.Write([byte]0)                 # reserviert
    $bw.Write([uint16]1)               # Ebenen
    $bw.Write([uint16]32)              # Bit-Tiefe
    $bw.Write([uint32]$daten.Length)
    $bw.Write([uint32]$offset)
    $offset += $daten.Length
}
foreach ($eintrag in $pngs) { $bw.Write($eintrag[1]) }
$bw.Close()

# Gegenprobe: lässt sich die Datei als Icon laden?
$test = New-Object System.Drawing.Icon($zielDatei)
"Icon erstellt: $zielDatei ($([math]::Round((Get-Item $zielDatei).Length / 1KB)) KB), Standardgröße $($test.Width)x$($test.Height)"
$test.Dispose()
