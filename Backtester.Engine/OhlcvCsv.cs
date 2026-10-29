using System.Globalization;
using Pine.Core.Runtime;

namespace Backtester.Engine;

public sealed class CsvFormatException(string message, int line) : Exception($"{message} (line {line})")
{
    public int Line { get; } = line;
}

/// <summary>
/// Reads OHLCV bars from CSV with a header row (TradingView's chart export works as is).
/// Columns are found by name, case-insensitively: <c>time</c>/<c>date</c>/<c>datetime</c>/<c>timestamp</c>,
/// <c>open</c>, <c>high</c>, <c>low</c>, <c>close</c> and optionally <c>volume</c>; other columns are ignored.
/// The separator is <c>,</c>, <c>;</c> or tab, detected from the header. Time is unix seconds or
/// milliseconds, or a date/time string (UTC unless it carries an offset). Bars are sorted by time.
/// </summary>
public static class OhlcvCsv
{
    private static readonly string[] TimeNames = ["time", "date", "datetime", "timestamp"];

    public static List<Bar> ReadFile(string path)
    {
        using var reader = new StreamReader(path);
        return Read(reader);
    }

    public static List<Bar> Parse(string csv)
        => Read(new StringReader(csv));

    public static List<Bar> Read(TextReader reader)
    {
        var header = reader.ReadLine() ?? throw new CsvFormatException("Empty file", 1);
        var separator = header.Contains(';') ? ';' : header.Contains('\t') ? '\t' : ',';
        var names = header.Split(separator).Select(n => n.Trim().Trim('"').ToLowerInvariant()).ToArray();

        int Column(string name, bool required = true)
        {
            var index = Array.IndexOf(names, name);
            if (index < 0 && required)
                throw new CsvFormatException($"Missing column '{name}'", 1);
            return index;
        }

        var time = Array.FindIndex(names, n => TimeNames.Contains(n));
        if (time < 0)
            throw new CsvFormatException("Missing time column (time, date, datetime or timestamp)", 1);

        var open = Column("open");
        var high = Column("high");
        var low = Column("low");
        var close = Column("close");
        var volume = Column("volume", required: false);

        var bars = new List<Bar>();
        var lineNumber = 1;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var fields = line.Split(separator);
            if (fields.Length < names.Length)
                throw new CsvFormatException($"Expected {names.Length} fields, got {fields.Length}", lineNumber);

            double Number(int index)
                => double.TryParse(fields[index].Trim().Trim('"'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    ? value
                    : throw new CsvFormatException($"Invalid number '{fields[index]}' in column '{names[index]}'", lineNumber);

            bars.Add(new Bar(
                ParseTime(fields[time].Trim().Trim('"'), lineNumber),
                Number(open), Number(high), Number(low), Number(close),
                volume >= 0 && fields[volume].Trim().Length > 0 ? Number(volume) : 0));
        }

        bars.Sort((a, b) => a.Time.CompareTo(b.Time));
        return bars;
    }

    /// <returns>Unix milliseconds.</returns>
    private static long ParseTime(string text, int line)
    {
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            // Seconds until the year 5138; anything larger is already milliseconds
            return number < 100_000_000_000 ? number * 1000 : number;

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
            return date.ToUnixTimeMilliseconds();

        throw new CsvFormatException($"Invalid time '{text}'", line);
    }
}
