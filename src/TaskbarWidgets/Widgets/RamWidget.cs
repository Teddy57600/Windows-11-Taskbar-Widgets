using System.Runtime.InteropServices;
using System.Windows.Media;
using TaskbarWidgets.Sdk;

namespace TaskbarWidgets.Widgets;

/// <summary>Mémoire physique utilisée.</summary>
public sealed class RamWidget : SystemWidget
{
    public override string Id => "ram";
    public override string DisplayName => "Mémoire";
    public override string DefaultLabel => "RAM";
    public override int DefaultOrder => 20;
    public override Color DefaultAccent => Color.FromRgb(0xD8, 0x9C, 0xF6);

    public override void Update()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status))
            return;

        double total = status.ullTotalPhys;
        double used = total - status.ullAvailPhys;
        double ratio = total > 0 ? used / total : 0;

        Value = $"{ratio * 100:0}%";
        Level = ratio;
        ToolTip = $"Mémoire : {Units.Bytes(used)} utilisés sur {Units.Bytes(total)}";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
}
