param([string[]]$Paths)

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectDir = Get-ChildItem -LiteralPath $repoRoot -Directory |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName "Assets") } |
    Select-Object -First 1

if (($null -eq $Paths -or $Paths.Count -eq 0) -and $null -ne $projectDir) {
    $assets = Join-Path $projectDir.FullName "Assets"
    $Paths = @(
        (Join-Path $assets "Branding"),
        (Join-Path $assets "UI\Icons"),
        (Join-Path $assets "UI\Illustrations"),
        (Join-Path $assets "Controllers\Xbox\Buttons"),
        (Join-Path $assets "Controllers\DSE\Buttons")
    )
}

Add-Type -AssemblyName System.Drawing

foreach ($path in $Paths) {
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Output "MISSING`t$path"
        continue
    }

    Get-ChildItem -LiteralPath $path -Filter *.png -File -ErrorAction SilentlyContinue | ForEach-Object {
        $bitmap = $null
        try {
            $bitmap = [System.Drawing.Bitmap]::FromFile($_.FullName)
            $hasAlphaFormat = [System.Drawing.Image]::IsAlphaPixelFormat($bitmap.PixelFormat)
            $minAlpha = 255
            $maxAlpha = 0
            $transparentPixels = 0
            for ($y = 0; $y -lt $bitmap.Height; $y++) {
                for ($x = 0; $x -lt $bitmap.Width; $x++) {
                    $alpha = $bitmap.GetPixel($x, $y).A
                    if ($alpha -lt $minAlpha) { $minAlpha = $alpha }
                    if ($alpha -gt $maxAlpha) { $maxAlpha = $alpha }
                    if ($alpha -lt 10) { $transparentPixels++ }
                }
            }

            $status = if (-not $hasAlphaFormat -or $minAlpha -eq 255) {
                "OPAQUE"
            } elseif ($transparentPixels -gt 0) {
                "TRANSPARENT"
            } else {
                "ALPHA_NO_CLEAR_PIXELS"
            }

            Write-Output "$status`t$($_.FullName)`tFormat=$($bitmap.PixelFormat)`tMinAlpha=$minAlpha`tMaxAlpha=$maxAlpha`tClearPixels=$transparentPixels"
        }
        catch {
            Write-Output "ERROR`t$($_.FullName)`t$($_.Exception.Message)"
        }
        finally {
            if ($bitmap -ne $null) {
                $bitmap.Dispose()
            }
        }
    }
}
