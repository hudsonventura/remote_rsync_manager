using Microsoft.EntityFrameworkCore;
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
            optionsBuilder.UseNpgsql(connectionString);
            _logger?.LogInformation("Connection string configured from IConfiguration");
        }
    }

    public DbSet<Agent> Agents { get; set; }
    public DbSet<BackupPlan> BackupPlans { get; set; }
}

