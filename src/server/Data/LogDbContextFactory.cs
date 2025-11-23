using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace server.Data;

public class LogDbContextFactory : IDesignTimeDbContextFactory<LogDbContext>
{
    public LogDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<LogDbContext>();
        optionsBuilder.UseSqlite("Data Source=./data/logs.db");

        return new LogDbContext(optionsBuilder.Options);
    }
}

