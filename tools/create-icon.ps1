Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$targetFile = "$env:USERPROFILE\Documents\WindowKeeper\icon.ico"
$sizes = 16, 20, 24, 32, 48, 64, 128, 256
$pngs = @()

foreach ($sz in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($sz, $sz, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $s = $sz / 16.0

    # window frame with title bar (white, matching Windows 11 tray icons)
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, [float](1.4 * $s))
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $g.DrawRectangle($pen, [float](1.5 * $s), [float](2.5 * $s), [float](13.0 * $s), [float](11.0 * $s))
    $g.DrawLine($pen, [float](1.5 * $s), [float](5.5 * $s), [float](14.5 * $s), [float](5.5 * $s))

    # accent-colored position dot in the middle of the client area
    $brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 76, 194, 255))
    $radius = 2.4 * $s
    $g.FillEllipse($brush, [float](8.0 * $s - $radius), [float](9.5 * $s - $radius), [float](2 * $radius), [float](2 * $radius))

    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $pngs += ,@($sz, $ms.ToArray())
    $ms.Dispose()
}

# write the ICO container (PNG entries, supported since Vista)
$fs = [System.IO.File]::Create($targetFile)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0)                 # reserved
$bw.Write([uint16]1)                 # type: icon
$bw.Write([uint16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
foreach ($entry in $pngs) {
    $sz = $entry[0]; $data = $entry[1]
    $bw.Write([byte]($sz -band 0xFF))  # 256 -> 0
    $bw.Write([byte]($sz -band 0xFF))
    $bw.Write([byte]0)                 # color count
    $bw.Write([byte]0)                 # reserved
    $bw.Write([uint16]1)               # planes
    $bw.Write([uint16]32)              # bit depth
    $bw.Write([uint32]$data.Length)
    $bw.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($entry in $pngs) { $bw.Write($entry[1]) }
$bw.Close()

# sanity check: does the file load as an icon?
$test = New-Object System.Drawing.Icon($targetFile)
"Icon created: $targetFile ($([math]::Round((Get-Item $targetFile).Length / 1KB)) KB), default size $($test.Width)x$($test.Height)"
$test.Dispose()
