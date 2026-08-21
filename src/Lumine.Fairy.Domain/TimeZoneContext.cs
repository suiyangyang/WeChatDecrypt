using System.Globalization;

namespace Lumine.Fairy.Domain;

/// <summary>
/// Provides one configured time zone for all user-facing time conversions.
/// Stored timestamps remain UTC; this context is only responsible for conversion and formatting.
/// </summary>
public sealed class TimeZoneContext
{
    public const string DefaultTimeZoneId = "China Standard Time";

    public TimeZoneContext(string? timeZoneId = null)
    {
        Current = Resolve(timeZoneId);
    }

    public TimeZoneInfo Current { get; private set; }

    public string Id => Current.Id;

    public void SetTimeZone(string? timeZoneId)
        => Current = Resolve(timeZoneId);

    public DateTimeOffset Convert(DateTimeOffset value)
        => TimeZoneInfo.ConvertTime(value, Current);

    public DateTimeOffset? FromUnixTime(long value)
    {
        try
        {
            var utc = value >= 100_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(value)
                : DateTimeOffset.FromUnixTimeSeconds(value);
            return Convert(utc);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public string Format(DateTimeOffset value, string format = "yyyy-MM-dd HH:mm")
        => Convert(value).ToString(format, CultureInfo.CurrentCulture);

    public string FormatCurrent(string format = "yyyy-MM-dd HH:mm:ss")
        => Format(DateTimeOffset.UtcNow, format);

    public static TimeZoneInfo Resolve(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZoneId);
        }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        }
        catch (TimeZoneNotFoundException) { }
        catch (InvalidTimeZoneException) { }

        return TimeZoneInfo.Utc;
    }
}
