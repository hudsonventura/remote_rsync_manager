using Microsoft.EntityFrameworkCore;
using server.Models;

namespace server.Data;

public class LogDbContext : DbContext
{
    public DbSet<LogEntry> LogEntries { get; set; }
    public DbSet<BackupExecution> BackupExecutions { get; set; }
    public DbSet<ExtensionExecution> ExtensionExecutions { get; set; }

    public LogDbContext(DbContextOptions<LogDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<BackupExecution>(entity =>
        {
            entity.HasKey(e => e.id);
            entity.Property(e => e.id).ValueGeneratedNever();
            entity.Property(e => e.backupPlanId).IsRequired();
            entity.Property(e => e.name).IsRequired().HasMaxLength(500);
            entity.Property(e => e.startDateTime).IsRequired();
            
            // Create index on backupPlanId and startDateTime for faster queries
            entity.HasIndex(e => new { e.backupPlanId, e.startDateTime });
        });

        modelBuilder.Entity<LogEntry>(entity =>
        {
            entity.HasKey(e => e.id);
            entity.Property(e => e.id).ValueGeneratedNever();
            entity.Property(e => e.backupPlanId).IsRequired();
            entity.Property(e => e.executionId).IsRequired();
            entity.Property(e => e.datetime).IsRequired();
            entity.Property(e => e.fileName).IsRequired().HasMaxLength(500);
            entity.Property(e => e.filePath).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.action).IsRequired().HasMaxLength(50);
            entity.Property(e => e.reason).IsRequired().HasMaxLength(500);

            // Create index on backupPlanId and datetime for faster queries
            entity.HasIndex(e => new { e.backupPlanId, e.datetime });
            // Create index on executionId for faster queries
            entity.HasIndex(e => e.executionId);
        });

        modelBuilder.Entity<ExtensionExecution>(entity =>
        {
            entity.HasKey(e => e.id);
            entity.Property(e => e.id).ValueGeneratedNever();
            entity.Property(e => e.extensionId).IsRequired();
            entity.Property(e => e.extensionName).IsRequired().HasMaxLength(500);
            entity.Property(e => e.command).IsRequired().HasMaxLength(500);
            entity.Property(e => e.startDateTime).IsRequired();
            entity.Property(e => e.status).IsRequired().HasMaxLength(50);
            
            // Create index on extensionId and startDateTime for faster queries
            entity.HasIndex(e => new { e.extensionId, e.startDateTime });
        });
    }
}

