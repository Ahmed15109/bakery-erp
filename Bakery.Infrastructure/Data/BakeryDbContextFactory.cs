using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bakery.Infrastructure.Data;

public sealed class BakeryDbContextFactory : IDesignTimeDbContextFactory<BakeryDbContext>
{
    public BakeryDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BakeryDbContext>();
        optionsBuilder.UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=BakeryERP;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True");

        return new BakeryDbContext(optionsBuilder.Options);
    }
}
