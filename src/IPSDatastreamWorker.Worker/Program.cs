using IPSDatastreamWorker.Infrastructure;

var builder = Host.CreateApplicationBuilder(args);

// Add Infrastructure services (DbContext, Redis, Background Worker, etc.)
builder.Services.AddInfrastructure(builder.Configuration);

var host = builder.Build();

// Log startup
var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("IPSDatastream Worker Service starting up...");
logger.LogInformation("Environment: {Environment}", builder.Environment.EnvironmentName);
logger.LogInformation("This service performs quality checks on completed sessions");

host.Run();

