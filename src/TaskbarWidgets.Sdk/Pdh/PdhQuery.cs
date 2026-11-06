using System.Runtime.InteropServices;

namespace TaskbarWidgets.Sdk.Pdh;

/// <summary>
/// Petite enveloppe autour de l'API PDH (compteurs de performance Windows).
/// </summary>
/// <remarks>
/// Les chemins sont en anglais (PdhAddEnglishCounter) : ils fonctionnent quelle que soit la langue de Windows.
/// Les caractères génériques sont acceptés dans l'instance, ex. <c>\GPU Engine(*)\Utilization Percentage</c>.
/// Les compteurs de type « taux » ne renvoient une valeur qu'à partir de la deuxième collecte.
/// </remarks>
public sealed class PdhQuery : IDisposable
{
    private IntPtr _query;

    public PdhQuery()
    {
        uint status = NativePdh.PdhOpenQueryW(null, IntPtr.Zero, out _query);
        if (status != 0)
            throw new InvalidOperationException($"PdhOpenQuery a échoué (0x{status:X8}).");
    }

    /// <summary>Ajoute un compteur ; renvoie <c>null</c> s'il n'existe pas sur cette machine.</summary>
    public PdhCounter? TryAddCounter(string englishPath)
    {
        uint status = NativePdh.PdhAddEnglishCounterW(_query, englishPath, IntPtr.Zero, out var counter);
        return status == 0 ? new PdhCounter(counter, englishPath) : null;
    }

    /// <summary>Collecte un nouvel échantillon pour tous les compteurs de la requête.</summary>
    public bool Collect() => NativePdh.PdhCollectQueryData(_query) == 0;

    public void Dispose()
    {
        if (_query != IntPtr.Zero)
        {
            NativePdh.PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
    }
}

/// <summary>Un compteur appartenant à une <see cref="PdhQuery"/>.</summary>
public sealed class PdhCounter
{
    private const int ItemSize = 24;      // sizeof(PDH_FMT_COUNTERVALUE_ITEM_W), x86 comme x64
    private const int ItemStatusOffset = 8;
    private const int ItemValueOffset = 16;

    private readonly IntPtr _handle;

    internal PdhCounter(IntPtr handle, string path)
    {
        _handle = handle;
        Path = path;
    }

    public string Path { get; }

    /// <summary>Valeur d'un compteur sans caractère générique ; <c>null</c> si indisponible.</summary>
    public double? GetValue()
    {
        uint status = NativePdh.PdhGetFormattedCounterValue(
            _handle, NativePdh.PDH_FMT_DOUBLE | NativePdh.PDH_FMT_NOCAP100, out _, out var value);
        return status == 0 && value.CStatus <= 1 ? value.DoubleValue : null;
    }

    /// <summary>Valeurs de toutes les instances (compteur avec caractère générique).</summary>
    public IReadOnlyList<PdhInstanceValue> GetValues()
    {
        const uint format = NativePdh.PDH_FMT_DOUBLE | NativePdh.PDH_FMT_NOCAP100;
        uint size = 0;
        uint status = NativePdh.PdhGetFormattedCounterArrayW(_handle, format, ref size, out _, IntPtr.Zero);
        if (status != NativePdh.PDH_MORE_DATA || size == 0)
            return [];

        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            status = NativePdh.PdhGetFormattedCounterArrayW(_handle, format, ref size, out uint count, buffer);
            if (status != 0)
                return [];

            var result = new List<PdhInstanceValue>((int)count);
            for (int i = 0; i < count; i++)
            {
                IntPtr item = buffer + i * ItemSize;
                if (Marshal.ReadInt32(item, ItemStatusOffset) > 1)
                    continue;
                string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(item)) ?? "";
                double value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(item, ItemValueOffset));
                result.Add(new PdhInstanceValue(name, value));
            }
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}

public readonly record struct PdhInstanceValue(string Instance, double Value);

internal static class NativePdh
{
    public const uint PDH_FMT_DOUBLE = 0x00000200;
    public const uint PDH_FMT_NOCAP100 = 0x00008000;
    public const uint PDH_MORE_DATA = 0x800007D2;

    [StructLayout(LayoutKind.Explicit)]
    public struct PdhFmtCounterValue
    {
        [FieldOffset(0)] public uint CStatus;
        [FieldOffset(8)] public double DoubleValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern uint PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern uint PdhAddEnglishCounterW(IntPtr query, string fullCounterPath, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    public static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll")]
    public static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PdhFmtCounterValue value);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    public static extern uint PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    public static extern uint PdhCloseQuery(IntPtr query);
}
