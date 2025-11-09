using Microsoft.EntityFrameworkCore;
using server.Models;

namespace server.Data;

public class DBContext : DbContext
{
    public DBContext(DbContextOptions<DBContext> options)
        : base(options)
    {
    }

    public DbSet<Agent> Agents { get; set; }
    public DbSet<BackupPlan> BackupPlans { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Agent entity
        modelBuilder.Entity<Agent>(entity =>
        {
            entity.HasKey(e => e.id);
            entity.Property(e => e.hostname)
                .IsRequired()
                .HasMaxLength(255);
        });

        // Configure BackupPlan entity
        modelBuilder.Entity<BackupPlan>(entity =>
        {
            entity.HasKey(e => e.id);
            entity.Property(e => e.name)
                .IsRequired()
                .HasMaxLength(255);
            entity.Property(e => e.description)
                .HasMaxLength(1000);
            entity.Property(e => e.schedule)
                .IsRequired()
                .HasMaxLength(100);
            entity.Property(e => e.source)
                .IsRequired()
                .HasMaxLength(1000);
            entity.Property(e => e.destination)
                .IsRequired()
                .HasMaxLength(1000);

            // Configure relationship if needed
            entity.HasOne(e => e.agent)
                .WithMany()
                .HasForeignKey("agent_id")
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

