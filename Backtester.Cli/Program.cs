using Backtester.Engine;
using Backtester.Infrastructure.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddBacktester();

using var host = builder.Build();

var backtester = host.Services.GetRequiredService<Engine>();

// TODO: Avvio del backtest

Console.WriteLine("System Online!");
