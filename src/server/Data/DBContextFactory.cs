using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace server.Data;

public class DBContextFactory : IDesignTimeDbContextFactory<DBContext>
{
    public DBContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DBContext>();
        optionsBuilder.UseSqlite("Data Source=./data/server.db");

        return new DBContext(optionsBuilder.Options);
    }
}

