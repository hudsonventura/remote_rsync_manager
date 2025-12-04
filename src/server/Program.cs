using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using server.Data;
using server.HostedServices;
using server.Logging;
using server.Models;
using server.Services;
using System.Security.Cryptography;

var builder = WebApplication.CreateBuilder(args);

var workingDirectory = Directory.GetCurrentDirectory();


// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });
builder.Services.AddEndpointsApiExplorer();

// Configure CORS - Allow all origins
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Ensure data directory exists
var dataDirectory = Path.Combine(Directory.GetCurrentDirectory(), "data");
Console.WriteLine($"Data directory: {dataDirectory}");
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

// Configure Entity Framework Core with SQLite
var connectionString = "Data Source=data/server.db";

builder.Services.AddDbContext<DBContext>(options =>
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
using (var dbContext = new DBContext(new DbContextOptionsBuilder<DBContext>()
    .UseSqlite(ResolveDbPath(connectionString))
    .Options))
{
    // Apply pending migrations
    try
    {
        Console.WriteLine("Migrating database...");
        dbContext.Database.Migrate();
        Console.WriteLine("Database migrated successfully.");
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
        Console.WriteLine($"Generating certificate path: {dataDirectory}");
        var certPath = Path.Combine(dataDirectory, "server.pfx");
        Console.WriteLine($"Certificate path: {certPath}");
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
            Console.WriteLine($"Failed to generate certificate at {certConfig.certificatePath}: {ex.Message}");
            throw;
        }
    }

    // Configure Kestrel to use the certificate
    builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(int.Parse(Environment.GetEnvironmentVariable("ASPNETCORE_HTTPS_PORT") ?? "5001"), listenOptions =>
        {
            listenOptions.UseHttps(certConfig.certificatePath, certConfig.certificatePassword);
        });
    });
}

// Configure separate SQLite database for logs
var logsConnectionString = "Data Source=data/logs.db";

builder.Services.AddDbContext<LogDbContext>(options =>
    options.UseSqlite(ResolveDbPath(logsConnectionString)));

// Apply migrations to LogDbContext
using (var logContext = new LogDbContext(new DbContextOptionsBuilder<LogDbContext>()
    .UseSqlite(ResolveDbPath(logsConnectionString))
    .Options))
{
    try
    {
        Console.WriteLine("Migrating log database...");
        logContext.Database.Migrate();
        Console.WriteLine("Log database migrations applied successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error migrating log database: {ex.Message}");
        if (builder.Environment.IsDevelopment())
        {
            throw;
        }
    }
}

// Configure JWT Authentication - Get or create JWT configuration from database
string secretKey;
string issuer;
string audience;

using (var dbContext = new DBContext(new DbContextOptionsBuilder<DBContext>()
    .UseSqlite(ResolveDbPath(connectionString))
    .Options))
{
    var jwtConfig = dbContext.JwtConfigs.FirstOrDefault();

    if (jwtConfig == null)
    {
        // Generate a secure random JWT secret key (64 bytes = 512 bits)
        var randomBytes = new byte[64];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }
        var generatedSecretKey = Convert.ToBase64String(randomBytes);

        // Create default JWT configuration
        jwtConfig = new JwtConfig
        {
            secretKey = generatedSecretKey,
            issuer = "Remote Rsync Manager",
            audience = "RsyncAppUsers",
            created_at = DateTime.UtcNow,
            updated_at = DateTime.UtcNow
        };

        dbContext.JwtConfigs.Add(jwtConfig);
        dbContext.SaveChanges();

        Console.WriteLine($"JWT configuration created. Issuer: {jwtConfig.issuer}, Audience: {jwtConfig.audience}");
    }

    secretKey = jwtConfig.secretKey;
    issuer = jwtConfig.issuer;
    audience = jwtConfig.audience;

    // Seed default admin user if it doesn't exist
    var adminUser = dbContext.Users.FirstOrDefault(u => u.username == "admin");
    if (adminUser == null)
    {
        var adminPasswordHash = AuthService.HashPassword("admin");
        adminUser = new User
        {
            id = Guid.NewGuid(),
            username = "admin",
            email = "admin@rsync-manager.local",
            passwordHash = adminPasswordHash,
            isAdmin = true,
            isActive = true,
            createdAt = DateTime.UtcNow
        };

        dbContext.Users.Add(adminUser);
        dbContext.SaveChanges();

        Console.WriteLine("Default admin user created. Username: admin, Password: admin");
    }
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = issuer,
        ValidAudience = audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
    };
});

builder.Services.AddAuthorization();

// Register custom logger provider
// Clear default providers and add custom logger
builder.Logging.ClearProviders();
builder.Logging.AddProvider(new CustomLoggerProvider());

// Register application services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddSingleton<ITokenStore, TokenStore>();
builder.Services.AddScoped<BackupPlanExecutor>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<ITelegramService, TelegramService>();

// Register hosted services
builder.Services.AddHostedService<BackupRunner>();
builder.Services.AddHostedService<LogRetentionService>();
builder.Services.AddHostedService<TelegramHostedService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
// CORS must be before UseHttpsRedirection
app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
    // Disable HTTPS redirection in development to allow HTTP requests
    // app.UseHttpsRedirection();
}
else
{
    app.UseHttpsRedirection();
}

// Serve static files from wwwroot (React app build)
// This should be early but API routes will still take precedence via endpoint routing
app.UseStaticFiles();

// Log static files configuration for debugging
var wwwrootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
var logger = app.Services.GetRequiredService<ILogger<Program>>();
if (Directory.Exists(wwwrootPath))
{
    var files = Directory.GetFiles(wwwrootPath, "*", SearchOption.AllDirectories);
    logger.LogInformation("Static files enabled. Serving from: {Path} ({Count} files found)", wwwrootPath, files.Length);
}
else
{
    logger.LogWarning("wwwroot directory not found at: {Path}. Static files may not be served.", wwwrootPath);
}

app.UseAuthentication();
app.UseAuthorization();

// Map API controllers
app.MapControllers();

// Fallback to index.html for client-side routing (SPA)
// This must be last so all non-API routes serve the React app
app.MapFallbackToFile("index.html");


// Protected endpoint example
app.MapGet("/api/protected", () =>
{
    return Results.Ok(new { message = "This is a protected endpoint", timestamp = DateTime.UtcNow });
})
.RequireAuthorization()
.WithName("GetProtectedData");



app.Run();
