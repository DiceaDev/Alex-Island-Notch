using System.Runtime.InteropServices;
using AlexIsland.Core.SystemInfo;

namespace AlexIsland.App.Services;

/// <summary>
/// Lee el nivel de batería que Windows ya conoce de los accesorios Bluetooth
/// (el mismo que muestra Configuración › Bluetooth). Solo consulta propiedades
/// de dispositivos con CfgMgr32: no abre conexiones ni necesita permisos de administrador.
/// </summary>
internal static class BluetoothBatteryReader
{
    // Enumeradores donde viven los dispositivos Bluetooth clásicos, de bajo consumo y sus servicios.
    private static readonly string[] Enumerators = ["BTHENUM", "BTHLE", "BTHLEDEVICE"];

    private static readonly DevPropKey BatteryKey = new(new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), 2);
    private static readonly DevPropKey ConnectedKey = new(new Guid("83DA6326-97A6-4088-9453-A1923F573B29"), 15);
    private static readonly DevPropKey ContainerKey = new(new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), 2);
    private static readonly DevPropKey FriendlyNameKey = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);

    private const uint CmGetIdListFilterEnumerator = 0x1;
    private const uint CmGetIdListFilterPresent = 0x100;
    private const uint CrSuccess = 0;

    private const uint DevPropTypeByte = 0x3;
    private const uint DevPropTypeBoolean = 0x11;
    private const uint DevPropTypeGuid = 0xD;
    private const uint DevPropTypeString = 0x12;

    public static IReadOnlyList<BluetoothBatteryInfo> Read()
    {
        var nodes = new List<BluetoothNode>();
        foreach (var enumerator in Enumerators)
        {
            foreach (var id in DeviceIds(enumerator))
            {
                if (CM_Locate_DevNodeW(out var devInst, id, 0) != CrSuccess) continue;

                var container = ReadGuid(devInst, ContainerKey);
                if (container is not Guid containerId) continue;

                nodes.Add(new BluetoothNode(
                    id,
                    containerId,
                    ReadString(devInst, FriendlyNameKey),
                    ReadBool(devInst, ConnectedKey),
                    ReadByte(devInst, BatteryKey)));
            }
        }

        return BluetoothBattery.Combine(nodes);
    }

    private static IEnumerable<string> DeviceIds(string enumerator)
    {
        const uint flags = CmGetIdListFilterEnumerator | CmGetIdListFilterPresent;

        // La lista puede crecer entre la consulta del tamaño y la lectura; se reintenta.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (CM_Get_Device_ID_List_SizeW(out var length, enumerator, flags) != CrSuccess || length <= 1) return [];

            var buffer = new char[length];
            if (CM_Get_Device_ID_ListW(enumerator, buffer, length, flags) != CrSuccess) continue;

            return new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        }
        return [];
    }

    private static byte[]? ReadRaw(uint devInst, DevPropKey key, uint expectedType)
    {
        // La primera llamada solo pide el tamaño (responde "búfer pequeño"); la segunda lee el valor.
        var size = 0u;
        CM_Get_DevNode_PropertyW(devInst, in key, out var type, null, ref size, 0);
        if (size == 0 || type != expectedType) return null;

        var buffer = new byte[size];
        if (CM_Get_DevNode_PropertyW(devInst, in key, out type, buffer, ref size, 0) != CrSuccess || type != expectedType)
            return null;

        return buffer;
    }

    private static int? ReadByte(uint devInst, DevPropKey key)
        => ReadRaw(devInst, key, DevPropTypeByte) is { Length: >= 1 } b ? b[0] : null;

    private static bool? ReadBool(uint devInst, DevPropKey key)
        => ReadRaw(devInst, key, DevPropTypeBoolean) is { Length: >= 1 } b ? b[0] != 0 : null;

    private static Guid? ReadGuid(uint devInst, DevPropKey key)
        => ReadRaw(devInst, key, DevPropTypeGuid) is { Length: 16 } b ? new Guid(b) : null;

    private static string? ReadString(uint devInst, DevPropKey key)
        => ReadRaw(devInst, key, DevPropTypeString) is { } b
            ? System.Text.Encoding.Unicode.GetString(b).TrimEnd('\0')
            : null;

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DevPropKey(Guid fmtid, uint pid)
    {
        public readonly Guid FmtId = fmtid;
        public readonly uint Pid = pid;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_ID_List_SizeW(out uint length, string filter, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_ID_ListW(string filter, [Out] char[] buffer, uint length, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Locate_DevNodeW(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_DevNode_PropertyW(
        uint devInst, in DevPropKey key, out uint propertyType, byte[]? buffer, ref uint bufferSize, uint flags);
}
