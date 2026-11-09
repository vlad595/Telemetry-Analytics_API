using Microsoft.AspNetCore.OpenApi;
using Telemetry_Analytics_API.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();

var app = builder.Build();

app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "TelemetryApi");
    });
    
    app.MapGet("/", () => { return Results.Redirect("/swagger"); });
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
