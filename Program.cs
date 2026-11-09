var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwaggerUI();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "TelemetryApi");
    });
    
    app.MapGet("/", () => { return Results.Redirect("/swagger"); });
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
