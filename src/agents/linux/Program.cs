using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using AgentCommon.Data;
using AgentCommon.Models;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Register controllers from both this assembly and the common assembly
builder.Services.AddControllers()
    .AddApplicationPart(typeof(AgentCommon.Controllers.PongController).Assembly);
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Ensure data directory exists
var dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "data");
if (!Directory.Exists(dataDirectory))
{
    Directory.CreateDirectory(dataDirectory);
}

// Helper function to resolve database paths
string ResolveDbPath(string connectionString)
{
    if (connectionString.StartsWith("Data Source="))
    {
        var dbPath = connectionString.Substring("Data Source=".Length);
        // If path contains "data/", resolve it to the data directory
        if (dbPath.StartsWith("data/") || dbPath.StartsWith("data\\"))
        {
            var fileName = Path.GetFileName(dbPath);
            dbPath = Path.Combine(dataDirectory, fileName);
        }
        // If it's a relative path, make it relative to data directory
        else if (!Path.IsPathRooted(dbPath))
        {
            dbPath = Path.Combine(dataDirectory, dbPath);
        }
        return $"Data Source={dbPath}";
    }
    return connectionString;
}

// Configure SQLite database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=data/agent.db";

builder.Services.AddDbContext<AgentDbContext>(options =>
    options.UseSqlite(ResolveDbPath(connectionString)));

// Helper function to generate self-signed certificate
System.Security.Cryptography.X509Certificates.X509Certificate2 GenerateSelfSignedCertificate()
{
    using var rsa = System.Security.Cryptography.RSA.Create(2048);
    var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
        $"CN=localhost",
        rsa,
        System.Security.Cryptography.HashAlgorithmName.SHA256,
        System.Security.Cryptography.RSASignaturePadding.Pkcs1);
    
    request.CertificateExtensions.Add(
        new System.Security.Cryptography.X509Certificates.X509KeyUsageExtension(
            System.Security.Cryptography.X509Certificates.X509KeyUsageFlags.DigitalSignature |
            System.Security.Cryptography.X509Certificates.X509KeyUsageFlags.KeyEncipherment,
            false));
    
    request.CertificateExtensions.Add(
        new System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension(
            new System.Security.Cryptography.OidCollection {
                new System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1") // Server Authentication
            },
            false));
    
    var sanBuilder = new System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder();
    sanBuilder.AddDnsName("localhost");
    sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);
    request.CertificateExtensions.Add(sanBuilder.Build());
    
    var certificate = request.CreateSelfSigned(
        DateTimeOffset.UtcNow.AddDays(-1),
        DateTimeOffset.UtcNow.AddYears(1));
    
    return certificate;
}

// Run migrations and configure certificate before building the app
using (var dbContext = new AgentDbContext(new DbContextOptionsBuilder<AgentDbContext>()
    .UseSqlite(ResolveDbPath(connectionString))
    .Options))
{
    // Apply pending migrations
    try
    {
        dbContext.Database.Migrate();
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error migrating database: {ex.Message}");
        if (builder.Environment.IsDevelopment())
        {
            throw;
        }
    }
    
    // Get or create certificate configuration
    var certConfig = dbContext.CertificateConfigs.FirstOrDefault();
    
    if (certConfig == null)
    {
        // Generate random certificate password (32 characters)
        var randomBytes = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }
        var password = Convert.ToBase64String(randomBytes);
        
        // Generate certificate path
        var certPath = Path.Combine(dataDirectory, "agent.pfx");
        
        certConfig = new CertificateConfig
        {
            certificatePath = certPath,
            certificatePassword = password,
            created_at = DateTime.UtcNow,
            updated_at = DateTime.UtcNow
        };
        
        dbContext.CertificateConfigs.Add(certConfig);
        dbContext.SaveChanges();
        
        Console.WriteLine($"Certificate configuration created. Path: {certPath}");
    }
    
    // Generate self-signed certificate if it doesn't exist
    if (!File.Exists(certConfig.certificatePath))
    {
        try
        {
            var cert = GenerateSelfSignedCertificate();
            var certBytes = cert.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Pkcs12, certConfig.certificatePassword);
            File.WriteAllBytes(certConfig.certificatePath, certBytes);
            Console.WriteLine($"Generated and saved self-signed certificate at: {certConfig.certificatePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to generate certificate: {ex.Message}");
            throw;
        }
    }
    
    // Configure Kestrel to use the certificate
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(int.Parse(Environment.GetEnvironmentVariable("ASPNETCORE_HTTPS_PORT") ?? "5002"), listenOptions =>
        {
            listenOptions.UseHttps(certConfig.certificatePath, certConfig.certificatePassword);
        });
    });
}

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

// Use HTTPS redirection (certificate is configured via Kestrel)
app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

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
