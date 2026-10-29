using System.Globalization;
using System.Text.Json.Serialization;
using Scalar.AspNetCore;

// Form and query values are bound with the current culture: on an Italian machine "0.01" would
// read as 1 (dot = thousands separator). The API always uses invariant numbers.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
                    .AddJsonOptions(opts =>
                                    {
                                        opts.JsonSerializerOptions.Converters.Add(
                                            new JsonStringEnumConverter()
                                        );
                                        // Results may hold NaN (e.g. a trade's percent profit on a zero price)
                                        opts.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
                                    });
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
    app.Map("/", () => Results.Redirect("/scalar"));
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

// Documentation of the phases and the theory behind them
app.MapGet("/docs", () => Results.Content(Utils.Resources.ReadText("docs.html"), "text/html; charset=utf-8"))
   .ExcludeFromDescription();

app.Run();
