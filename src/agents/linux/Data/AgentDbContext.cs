using Microsoft.EntityFrameworkCore;
using agent.Models;

namespace agent.Data;

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

        modelBuilder.Entity<AgentToken>(entity =>
        {
            entity.HasKey(e => e.id);
            entity.Property(e => e.token).IsRequired().HasMaxLength(255);
            entity.Property(e => e.created_at).IsRequired();
        });

        modelBuilder.Entity<PairingCode>(entity =>
        {
            entity.HasKey(e => e.id);
            entity.Property(e => e.code).IsRequired().HasMaxLength(6);
            entity.Property(e => e.created_at).IsRequired();
            entity.Property(e => e.expires_at).IsRequired();
        });

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

