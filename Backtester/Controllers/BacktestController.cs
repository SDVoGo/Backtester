using System.IO.Compression;
using System.Text.Json;
using Backtester.Engine;
using Backtester.Engine.Reporting;
using Microsoft.AspNetCore.Mvc;
using Pine.Core.Lexing;
using Pine.Core.Parsing;
using Pine.Core.Runtime;

public sealed class BacktestForm
{
    /// <summary>PineScript strategy (.pine).</summary>
    public IFormFile? Script { get; set; }

    /// <summary>OHLCV bars (.csv) with a header: time, open, high, low, close[, volume].</summary>
    public required IFormFile Data { get; set; }

    /// <summary>Optional JSON object of input title → value, e.g. {"Fast SMA": 20}.</summary>
    public string? Inputs { get; set; }

    /// <summary>Price tick, unit of profit/loss/trail ticks and slippage.</summary>
    public double MinTick { get; set; } = 0.01;
}

/// <summary>Strategy run as JSON: everything in <see cref="BacktestResult"/> except the interpreter internals.</summary>
public sealed record BacktestResponse(
    StrategySettings Settings,
    StrategyMetrics Metrics,
    IReadOnlyDictionary<string, string> Inputs,
    IReadOnlyList<ClosedTrade> Trades,
    IReadOnlyList<Trade> OpenTrades);

[ApiController]
[Route("api/backtest")]
public class BacktestController : ControllerBase
{
    /// <summary>Runs a strategy on uploaded bars; returns settings, metrics and trades.</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    public Task<IActionResult> Run([FromForm] BacktestForm form)
        => Execute(form, null, Respond);

    /// <summary>Same as the JSON run, but returns a zip with trades.csv, equity.csv, metrics.csv and plots.csv.</summary>
    [HttpPost("csv")]
    [Consumes("multipart/form-data")]
    public Task<IActionResult> RunCsv([FromForm] BacktestForm form)
        => Execute(form, null, result => File(Zip(result), "application/zip", "backtest.zip"));

    /// <summary>Runs the sample strategy001.pine on uploaded bars (the script field is ignored).</summary>
    [HttpPost("strategy001")]
    [Consumes("multipart/form-data")]
    public Task<IActionResult> Strategy001([FromForm] BacktestForm form)
        => Execute(form, Utils.Resources.ReadFile("strategy001"), Respond);

    private OkObjectResult Respond(BacktestResult result)
        => Ok(new BacktestResponse(
            result.Settings,
            result.Metrics,
            result.Execution.Inputs.ToDictionary(i => i.Key, i => i.Value.ToString()),
            result.Trades,
            result.OpenTrades));

    private async Task<IActionResult> Execute(BacktestForm form, string? source, Func<BacktestResult, IActionResult> respond)
    {
        try
        {
            if (source is null)
            {
                if (form.Script is null)
                    return BadRequest(new { Message = "Missing script file" });
                source = await ReadText(form.Script);
            }

            var bars = OhlcvCsv.Parse(await ReadText(form.Data));
            var options = new BacktestOptions
            {
                MinTick = form.MinTick,
                Inputs = string.IsNullOrWhiteSpace(form.Inputs) ? null : InputValues.ParseJson(form.Inputs),
            };

            return respond(Backtest.Run(Parser.Parse(source), bars, options));
        }
        catch (LexerException ex)
        {
            return BadRequest(new { Stage = "lexer", ex.Message, ex.Line, ex.Column });
        }
        catch (ParseException ex)
        {
            return BadRequest(new { Stage = "parser", ex.Message, ex.Line, ex.Column });
        }
        catch (PineRuntimeException ex)
        {
            return BadRequest(new { Stage = "runtime", ex.Message, ex.Pos.Line, ex.Pos.Column, Bar = ex.BarIndex });
        }
        catch (CsvFormatException ex)
        {
            return BadRequest(new { Stage = "data", ex.Message, ex.Line });
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return BadRequest(new { Stage = "inputs", ex.Message });
        }
    }

    private static async Task<string> ReadText(IFormFile file)
    {
        using var reader = new StreamReader(file.OpenReadStream());
        return await reader.ReadToEndAsync();
    }

    private static byte[] Zip(BacktestResult result)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create))
        {
            void Add(string name, Action<BacktestResult, TextWriter> write)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                write(result, writer);
            }

            Add(CsvReport.TradesFile, CsvReport.WriteTrades);
            Add(CsvReport.EquityFile, CsvReport.WriteEquity);
            Add(CsvReport.MetricsFile, CsvReport.WriteMetrics);
            Add(CsvReport.PlotsFile, CsvReport.WritePlots);
        }
        return buffer.ToArray();
    }
}
