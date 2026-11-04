# Abre Alex Island en un runner de Windows, la recorre con el mouse y guarda
# capturas de pantalla. Es una revisión visual automática, no una prueba de
# aprobado/rechazado: el workflow falla solo si la app no arranca o se cae.

param(
    [Parameter(Mandatory = $true)] [string] $AppPath,
    [Parameter(Mandatory = $true)] [string] $OutDir,
    [double] $NotchScale = 1.0
)

$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
# SendInput simula un mouse real: WinUI necesita esos eventos para detectar el puntero.
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class UiInput {
    [StructLayout(LayoutKind.Sequential)]
    public struct MouseInput { public int Dx; public int Dy; public int Data; public uint Flags; public uint Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)]
    public struct Input { public uint Type; public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)]
    public struct Point { public int X; public int Y; }

    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point p);

    static uint Send(uint flags, int dx, int dy, int data) {
        var input = new Input { Type = 0, Mouse = new MouseInput { Dx = dx, Dy = dy, Data = data, Flags = flags } };
        var sent = SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Input)));
        if (sent == 0) Console.WriteLine("SendInput falló: error " + Marshal.GetLastWin32Error());
        return sent;
    }

    public static void MoveTo(int x, int y) {
        int w = GetSystemMetrics(0), h = GetSystemMetrics(1);
        // Coordenadas absolutas normalizadas a 0..65535 (MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE).
        Send(0x0001 | 0x8000, (int)Math.Round(x * 65535.0 / (w - 1)), (int)Math.Round(y * 65535.0 / (h - 1)), 0);
    }
    public static void LeftClick() { Send(0x0002, 0, 0, 0); Send(0x0004, 0, 0, 0); }
    public static void Wheel(int delta) { Send(0x0800, 0, 0, delta); }
}
"@
[UiInput]::SetProcessDPIAware() | Out-Null

$screen = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$cx = [int]($screen.Width / 2)
$s = $NotchScale
Write-Host "Pantalla: $($screen.Width)x$($screen.Height) · escala del notch: $s"

function Save-Shot([string] $name) {
    $bmp = New-Object System.Drawing.Bitmap $screen.Width, $screen.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($screen.Location, [System.Drawing.Point]::Empty, $screen.Size)
    $g.Dispose()

    # Recorte de la parte superior central (donde vive la isla), ampliado x2.
    $w = [Math]::Min(760, $screen.Width)
    $h = [Math]::Min(340, $screen.Height)
    $rect = New-Object System.Drawing.Rectangle ([int]($cx - $w / 2)), 0, $w, $h
    $crop = $bmp.Clone($rect, $bmp.PixelFormat)
    $zoom = New-Object System.Drawing.Bitmap ($w * 2), ($h * 2)
    $zg = [System.Drawing.Graphics]::FromImage($zoom)
    $zg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
    $zg.DrawImage($crop, 0, 0, $w * 2, $h * 2)
    $zg.Dispose()

    $zoom.Save((Join-Path $OutDir "$name.png"), [System.Drawing.Imaging.ImageFormat]::Png)
    $zoom.Dispose(); $crop.Dispose(); $bmp.Dispose()
    Write-Host "Captura: $name"
}

function Move-To([double] $x, [double] $y) {
    # Varios pasos cortos, como un mouse real, para que se generen eventos de entrada.
    $p = New-Object UiInput+Point
    [UiInput]::GetCursorPos([ref] $p) | Out-Null
    for ($i = 1; $i -le 8; $i++) {
        $ix = $p.X + ($x - $p.X) * $i / 8
        $iy = $p.Y + ($y - $p.Y) * $i / 8
        [UiInput]::MoveTo([int]$ix, [int]$iy)
        Start-Sleep -Milliseconds 25
    }
    [UiInput]::GetCursorPos([ref] $p) | Out-Null
    Write-Host "Cursor en ($($p.X), $($p.Y))"
}

function Click-At([double] $x, [double] $y) {
    Move-To $x $y
    Start-Sleep -Milliseconds 150
    [UiInput]::LeftClick()
}

function Wheel-Next {
    [UiInput]::Wheel(-120)
}

$startupLog = Join-Path $env:LOCALAPPDATA 'AlexIsland\logs\startup.log'
if (Test-Path $startupLog) { Remove-Item $startupLog -Force }

function Assert-Running($proc) {
    if ($proc.HasExited) {
        throw "Alex Island se cerró inesperadamente (código $($proc.ExitCode))."
    }
    # Si falla el arranque, la app sigue abierta mostrando un mensaje de error.
    if ((Test-Path $startupLog) -and (Select-String -Path $startupLog -Pattern 'ERROR|Excepción no controlada' -Quiet)) {
        Save-Shot 'error'
        throw "Alex Island registró un error: revisa startup.log."
    }
}

# Ajustes de partida: escala pedida y sin ocultar en pantalla completa.
$dataDir = Join-Path $env:LOCALAPPDATA 'AlexIsland'
New-Item -ItemType Directory -Force -Path $dataDir | Out-Null
@{
    NotchScale       = $s
    HideInFullscreen = $false
    ShowLyrics       = $false
} | ConvertTo-Json | Set-Content -Path (Join-Path $dataDir 'settings.json') -Encoding UTF8

# Cursor lejos de la isla antes de abrirla.
Move-To ($cx) ($screen.Height - 50)

$proc = Start-Process -FilePath $AppPath -PassThru
Start-Sleep -Seconds 4
Assert-Running $proc
Save-Shot '01-inicio'

# Tras unos segundos sin actividad entra en modo reposo (mascota).
Start-Sleep -Seconds 7
Assert-Running $proc
Save-Shot '02-reposo'

# Pasar el cursor sobre el notch: se expande (pestaña Música).
Move-To $cx (8 * $s)
Start-Sleep -Milliseconds 1500
Assert-Running $proc
Save-Shot '03-expandida-musica'

# Fijar la isla con un clic sobre el reloj de la cabecera (no es un botón).
$headerY = 17 * $s
Click-At ($cx - (440 / 2 - 60) * $s) $headerY
Start-Sleep -Milliseconds 800

# Rueda del mouse sobre la barra de pestañas para recorrer cada una.
$tabsX = $cx + (440 / 2 - 18 - 45) * $s
Move-To $tabsX $headerY
Start-Sleep -Milliseconds 400

$tabs = @('04-backlog', '05-agenda', '06-focus', '07-sistema', '08-shelf')
foreach ($tab in $tabs) {
    Wheel-Next
    Start-Sleep -Milliseconds 1800 # la pestaña Sistema necesita una lectura completa
    Assert-Running $proc
    Save-Shot $tab
}

# Vuelve a Música (la rueda da la vuelta) y suelta la isla.
Wheel-Next
Start-Sleep -Milliseconds 600
Click-At ($cx - (440 / 2 - 60) * $s) $headerY
Move-To $cx ($screen.Height - 50)
Start-Sleep -Seconds 2
Assert-Running $proc
Save-Shot '09-cerrada-de-nuevo'

Stop-Process -Id $proc.Id -Force
Write-Host "Listo: capturas en $OutDir"
