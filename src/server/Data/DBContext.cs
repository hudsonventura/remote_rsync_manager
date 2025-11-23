using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using server.Models;

namespace server.Data;

public class DBContext : DbContext
{
    private readonly IConfiguration? _configuration;
    private readonly ILogger<DBContext>? _logger;

    public DBContext(DbContextOptions<DBContext> options)
        : base(options)
    {
    }

    public DBContext(DbContextOptions<DBContext> options, IConfiguration configuration, ILogger<DBContext> logger)
        : base(options)
    {
        _configuration = configuration;
        _logger = logger;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured && _configuration != null)
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");
            optionsBuilder.UseSqlite(connectionString);
            _logger?.LogInformation("Connection string configured from IConfiguration");
        }
    }

    public DbSet<Agent> Agents { get; set; } = null!;
    public DbSet<BackupPlan> BackupPlans { get; set; } = null!;
    public DbSet<CertificateConfig> CertificateConfigs { get; set; } = null!;
    public DbSet<JwtConfig> JwtConfigs { get; set; } = null!;
    public DbSet<AppSettings> AppSettings { get; set; } = null!;
    public DbSet<Notification> Notifications { get; set; } = null!;
    public DbSet<User> Users { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CertificateConfig>(entity =>
        {
            entity.HasKey(e => e.id);
            entity.Property(e => e.certificatePath).IsRequired().HasMaxLength(500);
            entity.Property(e => e.certificatePassword).IsRequired().HasMaxLength(255);
            entity.Property(e => e.created_at).IsRequired();
            entity.Property(e => e.updated_at).IsRequired();
        });

        modelBuilder.Entity<JwtConfig>(entity =>
        {
            entity.HasKey(e => e.id);
            entity.Property(e => e.secretKey).IsRequired().HasMaxLength(500);
            entity.Property(e => e.issuer).IsRequired().HasMaxLength(255);
            entity.Property(e => e.audience).IsRequired().HasMaxLength(255);
            entity.Property(e => e.created_at).IsRequired();
            entity.Property(e => e.updated_at).IsRequired();
        });

        modelBuilder.Entity<AppSettings>(entity =>
        {
            entity.HasKey(e => e.id);
            entity.Property(e => e.key).IsRequired().HasMaxLength(255);
            entity.Property(e => e.value).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.created_at).IsRequired();
            entity.Property(e => e.updated_at).IsRequired();
            
            // Create unique index on key
            entity.HasIndex(e => e.key).IsUnique();
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.HasKey(e => e.id);
            entity.Property(e => e.id).ValueGeneratedNever();
            entity.Property(e => e.type).IsRequired().HasMaxLength(50);
            entity.Property(e => e.title).IsRequired().HasMaxLength(500);
            entity.Property(e => e.message).IsRequired().HasMaxLength(2000);
            entity.Property(e => e.createdAt).IsRequired();
            
            // Create index on createdAt and isRead for faster queries
            entity.HasIndex(e => new { e.createdAt, e.isRead });
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.id);
            entity.Property(e => e.id).ValueGeneratedNever();
            entity.Property(e => e.username).IsRequired().HasMaxLength(100);
            entity.Property(e => e.email).IsRequired().HasMaxLength(255);
            entity.Property(e => e.passwordHash).IsRequired().HasMaxLength(500);
            entity.Property(e => e.createdAt).IsRequired();
            entity.Property(e => e.timezone).HasMaxLength(100);
            entity.Property(e => e.theme).HasMaxLength(50);
            
            // Create unique index on username and email
            entity.HasIndex(e => e.username).IsUnique();
            entity.HasIndex(e => e.email).IsUnique();
        });
    }
}

