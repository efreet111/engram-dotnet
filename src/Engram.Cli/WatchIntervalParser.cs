namespace Engram.Cli;

/// <summary>
/// Parses interval strings for watch mode (30s, 5m, 1h).
/// ENG-208 Phase 9.
/// </summary>
public static class WatchIntervalParser
{
    /// <summary>
    /// Parses an interval string to a TimeSpan.
    /// </summary>
    /// <param name="input">Interval like "30s", "5m", "1h"</param>
    /// <returns>TimeSpan</returns>
    /// <exception cref="ArgumentException">Thrown when format is invalid</exception>
    public static TimeSpan Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return TimeSpan.FromSeconds(60); // Default 60s

        input = input.Trim();

        if (input.Length < 2)
            throw new ArgumentException($"invalid interval format: '{input}'. Use 30s, 5m, 1h", nameof(input));

        var suffix = input[^1];
        var numberPart = input[..^1];

        if (!int.TryParse(numberPart, out var value) || value <= 0)
            throw new ArgumentException($"invalid interval format: '{input}'. Use 30s, 5m, 1h", nameof(input));

        return suffix switch
        {
            's' => TimeSpan.FromSeconds(value),
            'm' => TimeSpan.FromMinutes(value),
            'h' => TimeSpan.FromHours(value),
            _ => throw new ArgumentException($"invalid interval suffix: '{suffix}'. Use s, m, or h", nameof(input)),
        };
    }

    /// <summary>
    /// Tries to parse an interval string.
    /// </summary>
    /// <returns>True if parsing succeeded</returns>
    public static bool TryParse(string? input, out TimeSpan result)
    {
        result = default;

        if (string.IsNullOrWhiteSpace(input))
        {
            result = TimeSpan.FromSeconds(60);
            return true;
        }

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
}