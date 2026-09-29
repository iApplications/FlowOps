using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "FlowOps API v1");
    });
}

app.MapGet("/api/health", () => Results.Ok(new ApiHealthResponse("Healthy", "FlowOps.Api")))
    .WithName("GetApiHealth")
    .WithSummary("Check the FlowOps API status")
    .WithTags("Health");

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});

app.Logger.LogInformation("FlowOps API starting in {Environment}", app.Environment.EnvironmentName);
app.Run();

internal sealed record ApiHealthResponse(string Status, string Service);
