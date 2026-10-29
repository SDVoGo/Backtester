using Backtester.Engine;

namespace Backtester.Engine.Tests;

public class OhlcvCsvTests
{
    [Fact]
    public void TradingViewExport_UnixSeconds()
    {
        var bars = OhlcvCsv.Parse("""
            time,open,high,low,close,Volume,Plot
            1700000060,2,3,1,2.5,100,7
            1700000000,1,2,0.5,1.5,50,7
            """);

        Assert.Equal(2, bars.Count);
        Assert.Equal(1_700_000_000_000, bars[0].Time);
        Assert.Equal((1.0, 2.0, 0.5, 1.5, 50.0), (bars[0].Open, bars[0].High, bars[0].Low, bars[0].Close, bars[0].Volume));
    }

    [Fact]
    public void IsoDates_Semicolons_NoVolume()
    {
        var bars = OhlcvCsv.Parse("""
            Date;Open;High;Low;Close
            2024-01-02T00:00:00Z;1;2;0.5;1.5
            2024-01-03;1.5;2;1;1.8
            """);

        Assert.Equal(new DateTimeOffset(2024, 1, 2, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), bars[0].Time);
        Assert.Equal(new DateTimeOffset(2024, 1, 3, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), bars[1].Time);
        Assert.Equal(0, bars[1].Volume);
    }

    [Theory]
    [InlineData("open,high,low,close\n1,2,0,1", "time column")]
    [InlineData("time,open,high,low\n1,1,2,0", "'close'")]
    [InlineData("time,open,high,low,close\n1,1,x,0,1", "line 2")]
    public void Errors(string csv, string message)
        => Assert.Contains(message, Assert.Throws<CsvFormatException>(() => OhlcvCsv.Parse(csv)).Message);
}
