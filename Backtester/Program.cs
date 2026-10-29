using System.Text.Json.Serialization;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
                    .AddJsonOptions(opts =>
                                    {
                                        opts.JsonSerializerOptions.Converters.Add(
                                            new JsonStringEnumConverter()
                                        );
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
