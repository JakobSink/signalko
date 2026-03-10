namespace Signalko.Web.Services;

/// <summary>
/// Skupne pomožne metode za pretvorbo HEX EPC vrednosti.
/// </summary>
public static class HexUtil
{
    /// <summary>
    /// Pretvori HEX EPC v ASCII. Vrne null če ni tiskljivih znakov.
    /// Striktna verzija: vse byte morajo biti printable (0x20–0x7E).
    /// </summary>
    public static string? HexToAsciiStrict(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex) || hex.Length % 2 != 0) return null;
        try
        {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            foreach (var b in bytes)
                if (b < 0x20 || b > 0x7E) return null;
            return System.Text.Encoding.ASCII.GetString(bytes);
        }
        catch { return null; }
    }

    /// <summary>
    /// Pretvori HEX EPC v ASCII. Ohrani samo tiskljive znake (lenient).
    /// Ignorira separatorje (presledek, pomišljaj, dvopičje).
    /// </summary>
    public static string? HexToAsciiLenient(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        hex = hex.Replace(" ", "").Replace("-", "").Replace(":", "");
        if (hex.Length % 2 != 0) hex = "0" + hex;

        var sb = new System.Text.StringBuilder();
        for (int i = 0; i + 1 < hex.Length; i += 2)
        {
            if (!byte.TryParse(hex.Substring(i, 2),
                    System.Globalization.NumberStyles.HexNumber, null, out byte b))
                continue;
            if (b >= 32 && b < 127) sb.Append((char)b);
        }
        var result = sb.ToString().Trim();
        return result.Length > 0 ? result : null;
    }
}
