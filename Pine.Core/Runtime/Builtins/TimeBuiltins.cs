using System.Globalization;
using System.Text.RegularExpressions;

namespace Pine.Core.Runtime.Builtins;

/// <summary>
/// <c>timestamp()</c> and calendar functions on unix-millisecond times. Without symbol info the
/// exchange timezone is UTC (true for crypto exchanges such as Binance); every function also
/// accepts an explicit timezone: <c>"UTC"</c>, <c>"GMT+2"</c>, <c>"UTC-03:30"</c> or an IANA name (<c>"Europe/Rome"</c>).
/// </summary>
internal static partial class TimeBuiltins
{
    private static readonly (string Name, Func<DateTime, int> Part)[] Parts =
    [
        ("year", d => d.Year),
        ("month", d => d.Month),
        ("dayofmonth", d => d.Day),
        ("dayofweek", d => (int)d.DayOfWeek + 1),   // Pine: Sunday = 1
        ("hour", d => d.Hour),
        ("minute", d => d.Minute),
        ("second", d => d.Second),
        ("weekofyear", d => ISOWeek.GetWeekOfYear(d)),
    ];

    private static readonly string[] Days = ["sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday"];

    public static void Register(BuiltinRegistry registry)
    {
        foreach (var (name, part) in Parts)
        {
            // year (variable, bar time) and year(time, timezone) (function)
            registry
                .Variable(name, (context, offset) => context.BarAt(offset) is { } bar ? Value.Int(part(Utc(bar.Time))) : Value.Na)
                .Function(name, ["time", "timezone"], call => call[0].IsNa
                    ? Value.Na
                    : Value.Int(part(Local(call, (long)call.Number(0), Zone(call, 1)))));
        }

        for (var i = 0; i < Days.Length; i++)
            registry.Constant($"dayofweek.{Days[i]}", Value.Int(i + 1));

        registry
            .Variable("time_close", (context, offset) => context.BarAt(offset) is { } bar ? Value.Int(bar.Time + BarDuration(context)) : Value.Na)
            .Variable("timenow", (context, _) => Value.Int(context.Bars[^1].Time))
            .Constant("syminfo.timezone", Value.String("UTC"))
            .Function("timestamp", ["timezone", "year", "month", "day", "hour", "minute", "second"], Timestamp, acceptsAnyArguments: true);
    }

    private static DateTime Utc(long unixMilliseconds)
        => DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds).UtcDateTime;

    private static DateTime Local(CallContext call, long unixMilliseconds, TimeZoneInfo? zone)
        => zone is null ? Utc(unixMilliseconds) : TimeZoneInfo.ConvertTimeFromUtc(Utc(unixMilliseconds), zone);

    /// <summary>Bar length from the first two bars (Pine's time_close is the next bar's open).</summary>
    private static long BarDuration(BarContext context)
        => context.Bars.Count > 1 ? context.Bars[1].Time - context.Bars[0].Time : 0;

    private static TimeZoneInfo? Zone(CallContext call, int index)
        => call.Has(index) && call[index].Kind == ValueKind.String ? ParseZone(call, call[index].AsString) : null;

    [GeneratedRegex(@"^(?:UTC|GMT)(?:([+-])(\d{1,2})(?::?(\d{2}))?)?$", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetZone();

    private static TimeZoneInfo ParseZone(CallContext call, string text)
    {
        var match = OffsetZone().Match(text.Trim());
        if (match.Success)
        {
            if (!match.Groups[1].Success)
                return TimeZoneInfo.Utc;

            var offset = new TimeSpan(int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                match.Groups[3].Success ? int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) : 0, 0);
            if (match.Groups[1].Value == "-")
                offset = -offset;
            return TimeZoneInfo.CreateCustomTimeZone(text, offset, text, text);
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(text);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw call.Error($"Unknown timezone '{text}'");
        }
    }

    /// <summary>
    /// <c>timestamp(year, month, day, hour, minute, second)</c>, the same with a leading timezone,
    /// or <c>timestamp("2024-01-15")</c> / <c>timestamp("2024-01-15T09:30:00+02:00")</c>.
    /// </summary>
    private static Value Timestamp(CallContext call)
    {
        // Positional arguments only: shift them when the first one is not a timezone
        var arguments = Enumerable.Range(0, call.Count).Where(call.Has).Select(i => call[i]).ToList();
        if (arguments.Count == 0)
            throw call.Error("timestamp() needs a date");

        if (arguments.Count == 1 && arguments[0].Kind == ValueKind.String)
            return ParseDate(call, arguments[0].AsString);

        TimeZoneInfo? zone = null;
        if (arguments[0].Kind == ValueKind.String)
        {
            zone = ParseZone(call, arguments[0].AsString);
            arguments.RemoveAt(0);
        }

        if (arguments.Count < 3 || arguments.Take(6).Any(a => !a.IsNumber))
            throw call.Error("timestamp() expects year, month, day[, hour, minute, second]");

        int Part(int index) => index < arguments.Count ? (int)arguments[index].AsInt : 0;

        // Out-of-range parts roll over, as in Pine (month 13 = January of the next year)
        var local = new DateTime(Part(0), 1, 1, 0, 0, 0, DateTimeKind.Unspecified)
            .AddMonths(Part(1) - 1).AddDays(Part(2) - 1)
            .AddHours(Part(3)).AddMinutes(Part(4)).AddSeconds(Part(5));

        var utc = zone is null ? DateTime.SpecifyKind(local, DateTimeKind.Utc) : TimeZoneInfo.ConvertTimeToUtc(local, zone);
        return Value.Int(new DateTimeOffset(utc).ToUnixTimeMilliseconds());
    }

    private static Value ParseDate(CallContext call, string text)
        => DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
               DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)
            ? Value.Int(date.ToUnixTimeMilliseconds())
            : throw call.Error($"Invalid date '{text}'");
}
