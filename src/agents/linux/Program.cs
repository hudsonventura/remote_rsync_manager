using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using agent.Data;
using agent.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Configure SQLite database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=agent.db";

builder.Services.AddDbContext<AgentDbContext>(options =>
    options.UseSqlite(connectionString));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // Standard OpenAPI endpoint
    app.MapOpenApi();
    
    // Custom OpenAPI endpoint with security scheme for Scalar
    app.MapGet("/openapi-auth.json", async (HttpContext context) =>
    {
        // Get the OpenAPI document service
        var openApiService = context.RequestServices.GetRequiredService<Microsoft.AspNetCore.Http.HttpContext>();
        
        // Fetch the standard OpenAPI document
        var httpClient = new System.Net.Http.HttpClient();
        var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
        var openApiResponse = await httpClient.GetStringAsync($"{baseUrl}/openapi/v1.json");
        
        // Parse JSON and add security scheme
        var jsonDoc = System.Text.Json.JsonDocument.Parse(openApiResponse);
        var root = jsonDoc.RootElement;
        var writer = new System.Text.Json.Utf8JsonWriter(new System.IO.MemoryStream());
        
        // Create modified JSON with security scheme
        using var stream = new System.IO.MemoryStream();
        using var jsonWriter = new System.Text.Json.Utf8JsonWriter(stream);
        
        jsonWriter.WriteStartObject();
        
        // Copy all existing properties
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Name == "components")
            {
                jsonWriter.WritePropertyName("components");
                jsonWriter.WriteStartObject();
                
                // Copy existing components
                if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    foreach (var compProp in prop.Value.EnumerateObject())
                    {
                        compProp.WriteTo(jsonWriter);
                    }
                }
                
                // Add security schemes
                jsonWriter.WritePropertyName("securitySchemes");
                jsonWriter.WriteStartObject();
                jsonWriter.WritePropertyName("AgentToken");
                jsonWriter.WriteStartObject();
                jsonWriter.WriteString("type", "apiKey");
                jsonWriter.WriteString("in", "header");
                jsonWriter.WriteString("name", "X-Agent-Token");
                jsonWriter.WriteString("description", "Agent authentication token. Get this token by pairing the agent with a pairing code.");
                jsonWriter.WriteEndObject();
                jsonWriter.WriteEndObject();
                
                jsonWriter.WriteEndObject();
            }
            else
            {
                prop.WriteTo(jsonWriter);
            }
        }
        
        // If components didn't exist, add it
        if (!root.TryGetProperty("components", out _))
        {
            jsonWriter.WritePropertyName("components");
            jsonWriter.WriteStartObject();
            jsonWriter.WritePropertyName("securitySchemes");
            jsonWriter.WriteStartObject();
            jsonWriter.WritePropertyName("AgentToken");
            jsonWriter.WriteStartObject();
            jsonWriter.WriteString("type", "apiKey");
            jsonWriter.WriteString("in", "header");
            jsonWriter.WriteString("name", "X-Agent-Token");
            jsonWriter.WriteString("description", "Agent authentication token. Get this token by pairing the agent with a pairing code.");
            jsonWriter.WriteEndObject();
            jsonWriter.WriteEndObject();
            jsonWriter.WriteEndObject();
        }
        
        jsonWriter.WriteEndObject();
        jsonWriter.Flush();
        
        stream.Position = 0;
        var modifiedJson = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        
        return Results.Content(modifiedJson, "application/json");
    });
    
    // Configure Scalar to use the custom OpenAPI document with auth
    app.MapScalarApiReference(options =>
    {
        options.AddHttpAuthentication("X-Agent-Token", scheme =>
        {
            scheme.Description = "Use your token";
        });
        options.AddPreferredSecuritySchemes("X-Agent-Token");

    });
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

// Apply pending migrations automatically
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
    var migrationLogger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        dbContext.Database.Migrate();
    }
    catch (Exception ex)
    {
        migrationLogger.LogError(ex, "An error occurred while migrating the database.");
        if (app.Environment.IsDevelopment())
        {
            throw;
        }
    }
}

// Generate initial pairing code on startup
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    // Check if there's an active code, if not generate one
    var hasActiveCode = dbContext.PairingCodes
        .Any(pc => pc.expires_at > DateTime.UtcNow);
    
    if (!hasActiveCode)
    {
        var random = new Random();
        var code = random.Next(100000, 999999).ToString();
        var expiresAt = DateTime.UtcNow.Add(TimeSpan.FromMinutes(10));

        var pairingCode = new PairingCode
        {
            code = code,
            created_at = DateTime.UtcNow,
            expires_at = expiresAt
        };

        dbContext.PairingCodes.Add(pairingCode);
        dbContext.SaveChanges();
        
        logger.LogInformation("=== PAIRING CODE GENERATED ===");
        logger.LogInformation("Code: {PairingCode}", code);
        logger.LogInformation("Valid for 10 minutes");
        logger.LogInformation("==============================");
        
        Console.WriteLine("\n========================================");
        Console.WriteLine("  PAIRING CODE: " + code);
        Console.WriteLine("  Valid for 10 minutes");
        Console.WriteLine("  Use this code to pair the agent");
        Console.WriteLine("========================================\n");
    }
    else
    {
        var activeCode = dbContext.PairingCodes
            .Where(pc => pc.expires_at > DateTime.UtcNow)
            .OrderByDescending(pc => pc.created_at)
            .First();
        
        var timeRemaining = activeCode.expires_at - DateTime.UtcNow;
        var minutesRemaining = (int)timeRemaining.TotalMinutes;
        
        logger.LogInformation("=== ACTIVE PAIRING CODE EXISTS ===");
        logger.LogInformation("Code: {PairingCode}", activeCode.code);
        logger.LogInformation("Expires at: {ExpiresAt}", activeCode.expires_at);
        logger.LogInformation("Time remaining: {MinutesRemaining} minutes", minutesRemaining);
        logger.LogInformation("==================================");
        
        Console.WriteLine("\n========================================");
        Console.WriteLine("  ⚠️  ACTIVE PAIRING CODE EXISTS");
        Console.WriteLine("  Code: " + activeCode.code);
        Console.WriteLine("  Expires at: " + activeCode.expires_at.ToString("yyyy-MM-dd HH:mm:ss UTC"));
        Console.WriteLine("  Time remaining: " + minutesRemaining + " minutes");
        Console.WriteLine("  Use this code to pair the agent");
        Console.WriteLine("========================================\n");
    }
}

app.Run();
