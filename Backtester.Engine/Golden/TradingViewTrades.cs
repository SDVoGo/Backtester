using System.Globalization;
using System.Text;

namespace Backtester.Engine.Golden;

/// <summary>One trade of TradingView's "List of trades". Times are in the chart's timezone, as exported.</summary>
public sealed record TradingViewTrade(
    int Number,
    Direction Direction,
    string? EntrySignal,
    DateTime EntryTime,
    double EntryPrice,
    string? ExitSignal,
    DateTime? ExitTime,
    double? ExitPrice,
    double Quantity,
    double? Profit)
{
    /// <summary>TradingView lists the open trade with exit signal "Open".</summary>
    public bool IsOpen => ExitTime is null || string.Equals(ExitSignal, "Open", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Reads the CSV export of the Strategy Tester's "List of trades". Each trade is two rows
/// (<c>Entry long</c> and <c>Exit long</c>) sharing the trade number. Columns are found by name prefix,
/// so both the older layout (<c>Contracts</c>, <c>Profit USD</c>) and the newer one
/// (<c>Position size (qty)</c>, <c>Net P&amp;L USD</c>) work, in any currency.
/// </summary>
public static class TradingViewTrades
{
    private static readonly string[] DateFormats = ["yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm"];

    public static List<TradingViewTrade> ReadFile(string path)
        => Parse(File.ReadAllText(path));

    public static List<TradingViewTrade> Parse(string csv)
    {
        var lines = csv.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0)
            throw new CsvFormatException("Empty file", 1);

        var separator = lines[0].Contains(';') && !lines[0].Contains(',') ? ';' : ',';
        var header = SplitFields(lines[0].TrimStart('﻿'), separator).Select(h => h.Trim().ToLowerInvariant()).ToArray();

        int Find(string what, params string[] prefixes)
        {
            var index = Array.FindIndex(header, h => prefixes.Any(p => h.StartsWith(p, StringComparison.Ordinal)) && !h.Contains('%'));
            return index >= 0 ? index : throw new CsvFormatException($"Missing column '{what}'", 1);
        }

        var number = Find("Trade #", "trade");
        var type = Find("Type", "type");
        var signal = Array.FindIndex(header, h => h.StartsWith("signal", StringComparison.Ordinal));
        var time = Find("Date/Time", "date");
        var price = Find("Price", "price");
        var quantity = Find("Contracts", "contracts", "position size (qty)", "quantity", "qty", "size");
        var profit = Array.FindIndex(header, h => (h.StartsWith("profit", StringComparison.Ordinal) || h.StartsWith("net p&l", StringComparison.Ordinal)) && !h.Contains('%'));

        var entries = new Dictionary<int, (Direction Direction, string? Signal, DateTime Time, double Price, double Quantity)>();
        var exits = new Dictionary<int, (string? Signal, DateTime Time, double Price, double? Profit)>();

        for (var i = 1; i < lines.Count; i++)
        {
            var line = i + 1;
            var fields = SplitFields(lines[i], separator);
            if (fields.Count < header.Length)
                throw new CsvFormatException($"Expected {header.Length} fields, got {fields.Count}", line);

            var tradeNumber = (int)ParseNumber(fields[number], line);
            var kind = fields[type].Trim().ToLowerInvariant();
            var direction = kind.Contains("short") ? Direction.Short : Direction.Long;
            var signalText = signal >= 0 ? fields[signal].Trim() : null;
            var at = ParseTime(fields[time], line);
            var fillPrice = ParseNumber(fields[price], line);

            if (kind.StartsWith("entry", StringComparison.Ordinal))
                entries[tradeNumber] = (direction, signalText, at, fillPrice, ParseNumber(fields[quantity], line));
            else if (kind.StartsWith("exit", StringComparison.Ordinal))
                exits[tradeNumber] = (signalText, at, fillPrice, profit >= 0 ? ParseNumber(fields[profit], line) : null);
            else
                throw new CsvFormatException($"Unknown trade type '{fields[type]}'", line);
        }

        return entries.OrderBy(e => e.Key).Select(e =>
        {
            var (direction, signalText, at, fillPrice, size) = e.Value;
            var hasExit = exits.TryGetValue(e.Key, out var exit);
            return new TradingViewTrade(e.Key, direction, signalText, at, fillPrice,
                hasExit ? exit.Signal : null, hasExit ? exit.Time : null, hasExit ? exit.Price : null, size,
                hasExit ? exit.Profit : null);
        }).ToList();
    }

    /// <summary>Numbers may carry thousands separators or a unicode minus.</summary>
    private static double ParseNumber(string text, int line)
    {
        var clean = text.Trim().Replace("−", "-").Replace(" ", "").Replace(" ", "");
        if (clean.Contains(',') && !clean.Contains('.'))
            clean = clean.Replace(',', '.');
        else
            clean = clean.Replace(",", "");

        return double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new CsvFormatException($"Invalid number '{text}'", line);
    }

    private static DateTime ParseTime(string text, int line)
        => DateTime.TryParseExact(text.Trim(), DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : throw new CsvFormatException($"Invalid date '{text}'", line);

    /// <summary>Splits one CSV line, honouring double-quoted fields.</summary>
    internal static List<string> SplitFields(string line, char separator)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                    quoted = false;
                else
                    field.Append(c);
            }
            else if (c == '"')
                quoted = true;
            else if (c == separator)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
                field.Append(c);
        }

        fields.Add(field.ToString());
        return fields;
    }
}
