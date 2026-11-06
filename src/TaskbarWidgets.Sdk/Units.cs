namespace TaskbarWidgets.Sdk;

/// <summary>Mise en forme compacte des valeurs.</summary>
public static class Units
{
    private static readonly string[] ByteUnits = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>Ex. 660 → « 660 B », 1 300 000 → « 1,2 MB ».</summary>
    public static string Bytes(double bytes)
    {
        int unit = 0;
        while (bytes >= 1000 && unit < ByteUnits.Length - 1)
        {
            bytes /= 1024;
            unit++;
        }
        string number = unit == 0 || bytes >= 10 ? bytes.ToString("0") : bytes.ToString("0.0");
        return $"{number} {ByteUnits[unit]}";
    }

    /// <summary>Ex. « 660 B/s ».</summary>
    public static string BytesPerSecond(double bytesPerSecond) => Bytes(bytesPerSecond) + "/s";
}
