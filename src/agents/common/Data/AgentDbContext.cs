using Microsoft.EntityFrameworkCore;
using AgentCommon.Models;

namespace AgentCommon.Data;

public class AgentDbContext : DbContext
{
    public AgentDbContext(DbContextOptions<AgentDbContext> options)
        : base(options)
    {
    }

    public DbSet<AgentToken> AgentTokens { get; set; }
    public DbSet<PairingCode> PairingCodes { get; set; }
    public DbSet<CertificateConfig> CertificateConfigs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure AgentToken
        modelBuilder.Entity<AgentToken>(entity =>
        {
            entity.ToTable("agent_token");
            entity.HasKey(e => e.id);
            entity.Property(e => e.token).IsRequired().HasMaxLength(255);
            entity.Property(e => e.created_at).IsRequired();
        });

        // Configure PairingCode
        modelBuilder.Entity<PairingCode>(entity =>
        {
            entity.ToTable("pairing_code");
            entity.HasKey(e => e.id);
            entity.Property(e => e.code).IsRequired();
            entity.Property(e => e.created_at).IsRequired();
            entity.Property(e => e.expires_at).IsRequired();
        });

        // Configure CertificateConfig
        modelBuilder.Entity<CertificateConfig>(entity =>
        {
            entity.ToTable("certificate_config");
            entity.HasKey(e => e.id);
            entity.Property(e => e.certificatePath).IsRequired();
            entity.Property(e => e.certificatePassword).IsRequired();
            entity.Property(e => e.created_at).IsRequired();
            entity.Property(e => e.updated_at).IsRequired();
        });
    }
}

