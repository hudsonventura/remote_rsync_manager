using Avalonia;
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;
using AgentCommon.Data;
using AgentCommon.Models;
using System.Security.Cryptography;

namespace agentWindows;

class Program
{
    private static CancellationTokenSource? _webServerCts;

    [STAThread]
    public static void Main(string[] args)
    {
        // Start the web server in a background thread
        _webServerCts = new CancellationTokenSource();
        var webServerTask = Task.Run(async () => await RunWebServerAsync(args, _webServerCts.Token));

        // Start the Avalonia UI on the main thread (runs in parallel with web server)
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            // When the UI closes, stop the web server gracefully
            Console.WriteLine("UI closed, stopping web server...");
            _webServerCts?.Cancel();
            try
            {
                webServerTask.Wait(TimeSpan.FromSeconds(5));
                Console.WriteLine("Web server stopped successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping web server: {ex.Message}");
            }
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    private static async Task RunWebServerAsync(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            Console.WriteLine("Starting web server...");
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            // Register controllers from both this assembly and the common assembly
            builder.Services.AddControllers()
                .AddApplicationPart(typeof(AgentCommon.Controllers.PongController).Assembly);
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
                    if (dbPath.StartsWith("data/") || dbPath.StartsWith("data\\"))
                    {
                        var fileName = Path.GetFileName(dbPath);
                        dbPath = Path.Combine(dataDirectory, fileName);
                    }
                    else if (!Path.IsPathRooted(dbPath))
                    {
                        dbPath = Path.Combine(dataDirectory, dbPath);
                    }
                    return $"Data Source={dbPath}";
                }
                return connectionString;
            }

            // Configure SQLite database
            var connectionString = builder.Configuration.GetSection("ConnectionStrings")["DefaultConnection"]
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
                app.MapOpenApi();
                
                app.MapGet("/openapi-auth.json", async (HttpContext context) =>
                {
                    var httpClient = new System.Net.Http.HttpClient();
                    var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
                    var openApiResponse = await httpClient.GetStringAsync($"{baseUrl}/openapi/v1.json");
                    
                    var jsonDoc = System.Text.Json.JsonDocument.Parse(openApiResponse);
                    var root = jsonDoc.RootElement;
                    
                    using var stream = new System.IO.MemoryStream();
                    using var jsonWriter = new System.Text.Json.Utf8JsonWriter(stream);
                    
                    jsonWriter.WriteStartObject();
                    
                    foreach (var prop in root.EnumerateObject())
                    {
                        if (prop.Name == "components")
                        {
                            jsonWriter.WritePropertyName("components");
                            jsonWriter.WriteStartObject();
                            
                            if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                            {
                                foreach (var compProp in prop.Value.EnumerateObject())
                                {
                                    compProp.WriteTo(jsonWriter);
                                }
                            }
                            
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

            // Generate initial pairing code on startup
            using (var scope = app.Services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AgentDbContext>();
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                
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

            Console.WriteLine("Web server started successfully. Listening on https://localhost:5002");
            
            // Run the web server with cancellation support
            await app.RunAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Web server shutdown requested.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Web server error: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
}
