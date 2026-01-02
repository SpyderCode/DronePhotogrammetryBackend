using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PhotogrammetryAPI.Data;

public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        
        // Use a dummy connection string for migrations
        optionsBuilder.UseMySql(
            "server=localhost;port=3306;database=photogrammetry;user=root;password=",
            new MySqlServerVersion(new Version(8, 0, 0)),
            options => options.EnableRetryOnFailure()
        );
        
        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
