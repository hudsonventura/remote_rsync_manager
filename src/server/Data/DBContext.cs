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

    public DbSet<Agent> Agents { get; set; }
    public DbSet<BackupPlan> BackupPlans { get; set; }
    public DbSet<CertificateConfig> CertificateConfigs { get; set; }
    
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
    }
}

