using System.Runtime.InteropServices;
using AlexIsland.Core.SystemInfo;

namespace AlexIsland.App.Services;

/// <summary>
/// Lee CPU, memoria, disco del sistema, tiempo encendido y batería usando
/// únicamente APIs que no requieren privilegios de administrador.
/// No guarda estado entre ejecuciones ni envía datos fuera del equipo.
/// </summary>
internal sealed class SystemMonitor
{
    private CpuTimes? _previousCpu;

    public SystemSnapshot Sample()
    {
        var cpu = SampleCpu();
        var (memUsed, memTotal) = SampleMemory();
        var (diskUsed, diskTotal, diskName) = SampleSystemDisk();
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        var (battery, charging) = SampleBattery();

        return new SystemSnapshot(cpu, memUsed, memTotal, diskUsed, diskTotal, diskName, uptime, battery, charging);
    }

    /// <summary>Descarta la lectura anterior para que la próxima muestra no mezcle intervalos largos.</summary>
    public void Reset() => _previousCpu = null;

    private double SampleCpu()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;

        var current = new CpuTimes(idle.ToUInt64(), kernel.ToUInt64(), user.ToUInt64());
        var result = _previousCpu is { } previous ? SystemMetrics.CpuPercent(previous, current) : 0;
        _previousCpu = current;
        return result;
    }

    private static (ulong Used, ulong Total) SampleMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0) return (0, 0);
        return (status.TotalPhys - status.AvailPhys, status.TotalPhys);
    }

    private static (ulong Used, ulong Total, string Name) SampleSystemDisk()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var drive = new DriveInfo(root);
            if (!drive.IsReady) return (0, 0, root.TrimEnd('\\'));

            var total = (ulong)Math.Max(0, drive.TotalSize);
            var free = (ulong)Math.Max(0, drive.TotalFreeSpace);
            return (total > free ? total - free : 0, total, root.TrimEnd('\\'));
        }
        catch
        {
            return (0, 0, "C:");
        }
    }

    private static (int? Percent, bool Charging) SampleBattery()
    {
        try
        {
            if (Windows.System.Power.PowerManager.BatteryStatus == Windows.System.Power.BatteryStatus.NotPresent)
            {
                return (null, false);
            }

            return (
                Windows.System.Power.PowerManager.RemainingChargePercent,
                Windows.System.Power.PowerManager.BatteryStatus == Windows.System.Power.BatteryStatus.Charging);
        }
        catch
        {
            return (null, false);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
        public readonly ulong ToUInt64() => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
