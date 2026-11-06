param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist\icon-resources'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$assets = @(
    @('network-recovery-app-v1.png', 'NetworkRecovery.App.ico'),
    @('network-recovery-enabled-v1.png', 'NetworkRecovery.Enabled.ico'),
    @('network-recovery-disabled-v1.png', 'NetworkRecovery.Disabled.ico')
)
foreach ($asset in $assets) {
    $source = [Drawing.Bitmap]::FromFile((Join-Path $PSScriptRoot ('src\assets\icons\' + $asset[0])))
    $frames = New-Object 'System.Collections.Generic.List[byte[]]'
    try {
        foreach ($size in $sizes) {
            $bitmap = New-Object Drawing.Bitmap($size, $size, ([Drawing.Imaging.PixelFormat]::Format32bppArgb))
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            $memory = New-Object IO.MemoryStream
            try {
                $graphics.Clear([Drawing.Color]::Transparent)
                $graphics.CompositingMode = [Drawing.Drawing2D.CompositingMode]::SourceCopy
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                $graphics.DrawImage($source, (New-Object Drawing.Rectangle(0, 0, $size, $size)))
                # Uncompressed 32-bit DIB frames work with both the Windows shell
                # and Framework Icon.ToBitmap, including small tray sizes.
                $pixels = New-Object byte[] ($size * $size * 4)
                $bits = $bitmap.LockBits((New-Object Drawing.Rectangle(0, 0, $size, $size)),
                    [Drawing.Imaging.ImageLockMode]::ReadOnly, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
                try { [Runtime.InteropServices.Marshal]::Copy($bits.Scan0, $pixels, 0, $pixels.Length) }
                finally { $bitmap.UnlockBits($bits) }
                $maskStride = [int]([Math]::Floor(($size + 31) / 32) * 4)
                $mask = New-Object byte[] ($maskStride * $size)
                $dib = New-Object IO.BinaryWriter($memory)
                $dib.Write([uint32]40); $dib.Write([int32]$size); $dib.Write([int32]($size * 2))
                $dib.Write([uint16]1); $dib.Write([uint16]32); $dib.Write([uint32]0)
                $dib.Write([uint32]($pixels.Length + $mask.Length))
                $dib.Write([int32]0); $dib.Write([int32]0); $dib.Write([uint32]0); $dib.Write([uint32]0)
                for ($row = 0; $row -lt $size; $row++) {
                    $sourceRow = $size - 1 - $row
                    $dib.Write($pixels, ($sourceRow * $size * 4), ($size * 4))
                    for ($column = 0; $column -lt $size; $column++) {
                        if ($pixels[($sourceRow * $size + $column) * 4 + 3] -eq 0) {
                            $maskIndex = $row * $maskStride + [int][Math]::Floor($column / 8)
                            $mask[$maskIndex] = [byte]($mask[$maskIndex] -bor (0x80 -shr ($column % 8)))
                        }
                    }
                }
                $dib.Write($mask); $dib.Flush()
                $frames.Add($memory.ToArray())
            } finally { $memory.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
        }
        $path = Join-Path $OutputDirectory $asset[1]
        $stream = [IO.File]::Create($path)
        $writer = New-Object IO.BinaryWriter($stream)
        try {
            $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
            $offset = [uint32](6 + 16 * $sizes.Count)
            for ($index = 0; $index -lt $sizes.Count; $index++) {
                $dimension = if ($sizes[$index] -eq 256) { [byte]0 } else { [byte]$sizes[$index] }
                $writer.Write($dimension); $writer.Write($dimension)
                $writer.Write([byte]0); $writer.Write([byte]0)
                $writer.Write([uint16]1); $writer.Write([uint16]32)
                $writer.Write([uint32]$frames[$index].Length); $writer.Write($offset)
                $offset += [uint32]$frames[$index].Length
            }
            foreach ($frame in $frames) { $writer.Write([byte[]]$frame) }
        } finally { $writer.Dispose(); $stream.Dispose() }
    } finally { $source.Dispose() }
}
Write-Output ('Created 3 Windows ICO resources with sizes ' + ($sizes -join ', ') + '.')
