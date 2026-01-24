// FathomVisual.LicenseServer/Program.cs
// ASP.NET Core application entry point

using FathomVisual.LicenseServer.Data;
using FathomVisual.LicenseServer.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

// Configure database based on environment
// Use PostgreSQL on Render.com (via DATABASE_URL), SQLite for local development
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
var isProduction = builder.Environment.IsProduction() || !string.IsNullOrEmpty(databaseUrl);

if (isProduction && !string.IsNullOrEmpty(databaseUrl))
{
    // Parse Render.com PostgreSQL connection string (format: postgres://user:password@host:port/database)
    var connectionString = ConvertPostgresUrlToConnectionString(databaseUrl);
    builder.Services.AddDbContext<LicenseDbContext>(options =>
        options.UseNpgsql(connectionString));

    Console.WriteLine("Using PostgreSQL database");
}
else
{
    // Use SQLite for local development
    var connectionString = builder.Configuration.GetConnectionString("LicenseDb")
        ?? "Data Source=licenses.db";

    builder.Services.AddDbContext<LicenseDbContext>(options =>
        options.UseSqlite(connectionString));

    Console.WriteLine($"Using SQLite database: {connectionString}");
}

// Register license generator service as singleton
builder.Services.AddSingleton<LicenseGeneratorService>();

// Add Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "FathomVisual License Server API",
        Version = "v1",
        Description = "API for license validation, activation, and management"
    });
});

// Configure CORS for License Manager desktop app
var corsOrigins = Environment.GetEnvironmentVariable("CORS_ORIGINS") ?? "*";
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (corsOrigins == "*")
        {
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
        else
        {
            policy.WithOrigins(corsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries))
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
    });
});

// Add health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<LicenseDbContext>();

var app = builder.Build();

// Ensure database is created and migrated
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<LicenseDbContext>();

    // For production, use migrations; for development, ensure created
    if (isProduction)
    {
        // Apply any pending migrations
        dbContext.Database.Migrate();
    }
    else
    {
        dbContext.Database.EnsureCreated();
    }
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "FathomVisual License Server API v1");
    });
}

// In production on Render.com, HTTPS is handled by the proxy
if (!isProduction)
{
    app.UseHttpsRedirection();
}

app.UseCors();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

// Log startup information
app.Logger.LogInformation("FathomVisual License Server starting...");
app.Logger.LogInformation("Environment: {Environment}", app.Environment.EnvironmentName);
app.Logger.LogInformation("Database Type: {DbType}", isProduction ? "PostgreSQL" : "SQLite");

app.Run();

/// <summary>
/// Converts a PostgreSQL URL (postgres://user:password@host:port/database) to a .NET connection string.
/// </summary>
static string ConvertPostgresUrlToConnectionString(string databaseUrl)
{
    var uri = new Uri(databaseUrl);
    var userInfo = uri.UserInfo.Split(':');
    var user = userInfo[0];
    var password = userInfo.Length > 1 ? userInfo[1] : "";
    var host = uri.Host;
    var port = uri.Port > 0 ? uri.Port : 5432;
    var database = uri.AbsolutePath.TrimStart('/');

    // Render.com requires SSL
    return $"Host={host};Port={port};Database={database};Username={user};Password={password};SSL Mode=Require;Trust Server Certificate=true";
}
