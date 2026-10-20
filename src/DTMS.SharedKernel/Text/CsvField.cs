namespace DTMS.SharedKernel.Text;

/// <summary>
/// One CSV cell for the report exports — RFC 4180 quoting plus a guard
/// against formula injection. Excel and LibreOffice run a cell that starts
/// with = + - @ (or a tab / CR) as a formula, and order refs, notes,
/// requester names and failure reasons reach these files from OMS and free
/// text. Such a cell is prefixed with ' (OWASP's advice), unless it is a
/// plain number such as -5.
/// </summary>
public static class CsvField
{
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var v = value;
        if ("=+-@\t\r".Contains(v[0]) && !IsPlainNumber(v)) v = "'" + v;
        var needsQuote = v.Contains(',') || v.Contains('"') || v.Contains('\n') || v.Contains('\r');
        return needsQuote ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
    }

    private static bool IsPlainNumber(string v)
        => double.TryParse(v, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _);
}
