namespace Engram.Cli;

/// <summary>
/// Parses --since argument values for obsidian-export.
/// Supports ISO 8601 dates and relative durations (30d, 7d, 24h, 5m).
/// ENG-208 Phase 8.
/// </summary>
public static class SinceArgumentParser
{
    /// <summary>
    /// Parses a --since argument value to a DateTime.
    /// </summary>
    /// <param name="input">ISO 8601 date (2025-01-01) or relative duration (30d, 7d, 24h, 5m)</param>
    /// <returns>DateTime in UTC</returns>
    /// <exception cref="ArgumentException">Thrown when format is invalid</exception>
    public static DateTime Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("since argument must not be empty", nameof(input));

        input = input.Trim();

        // Try ISO 8601 first
        if (TryParseIso8601(input, out var isoResult))
            return isoResult;

        // Try relative duration
        if (TryParseRelative(input, out var relativeResult))
            return relativeResult;

        throw new ArgumentException($"invalid --since format: '{input}'. Use ISO 8601 (2025-01-01) or relative (30d, 7d, 24h, 5m)", nameof(input));
    }

    /// <summary>
    /// Tries to parse a --since argument value.
    /// </summary>
    /// <returns>True if parsing succeeded</returns>
    public static bool TryParse(string? input, out DateTime result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        try
        {
            result = Parse(input);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool TryParseIso8601(string input, out DateTime result)
    {
        result = default;

        // Try various ISO 8601 formats
        string[] formats = [
            "yyyy-MM-ddTHH:mm:ssZ",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mmZ",
            "yyyy-MM-ddTHH:mm",
            "yyyy-MM-dd",
        ];

        if (DateTime.TryParseExact(input, formats, null,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out result))
        {
            return true;
        }

        // Also try general parse (handles many ISO formats)
        if (DateTime.TryParse(input, null,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out result))
        {
            return true;
        }

        return false;
    }

    private static bool TryParseRelative(string input, out DateTime result)
    {
        result = default;

        if (input.Length < 2)
            return false;

        var suffix = input[^1];
        var numberPart = input[..^1];

        if (!int.TryParse(numberPart, out var value) || value <= 0)
            return false;

        var now = DateTime.UtcNow;

        switch (suffix)
        {
            case 'd':
                result = now.AddDays(-value);
                return true;
            case 'h':
                result = now.AddHours(-value);
                return true;
            case 'm':
                result = now.AddMinutes(-value);
                return true;
            default:
                return false;
        }
    }
}