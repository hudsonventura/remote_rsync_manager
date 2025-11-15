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

var workingDirectory = Directory.GetCurrentDirectory()+ "teste";


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
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

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
        var certPath = Path.Combine(dataDirectory, "server.pfx");
        
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
        options.ListenAnyIP(int.Parse(Environment.GetEnvironmentVariable("ASPNETCORE_HTTPS_PORT") ?? "5001"), listenOptions =>
        {
            listenOptions.UseHttps(certConfig.certificatePath, certConfig.certificatePassword);
        });
    });
}

// Configure separate SQLite database for logs
var logsConnectionString = builder.Configuration.GetConnectionString("LogsConnection")
    ?? "Data Source=data/logs.db";

builder.Services.AddDbContext<LogDbContext>(options =>
    options.UseSqlite(ResolveDbPath(logsConnectionString)));

// Configure JWT Authentication
var jwtSettings = builder.Configuration.GetSection("Jwt");
var secretKey = jwtSettings["SecretKey"] ?? throw new InvalidOperationException("JWT SecretKey is not configured");

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
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
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

// Register hosted services
builder.Services.AddHostedService<BackupRunner>();

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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

// Protected endpoint example
app.MapGet("/api/protected", () =>
{
    return Results.Ok(new { message = "This is a protected endpoint", timestamp = DateTime.UtcNow });
})
.RequireAuthorization()
.WithName("GetProtectedData");



app.Run();
