using Microsoft.AspNetCore.OpenApi;
using Telemetry_Analytics_API.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddCors(options => options.AddPolicy("LocalWeb", policy => policy
    .WithOrigins("http://localhost:4200", "http://127.0.0.1:4200")
    .AllowAnyHeader()
    .AllowAnyMethod()));
builder.Services.AddHttpClient();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();
builder.Services.AddScoped<IAnalyticsAccessService, AnalyticsAccessService>();

var app = builder.Build();

app.UseCors("LocalWeb");
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
