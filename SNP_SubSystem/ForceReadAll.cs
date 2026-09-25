using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Sub_Entities.Entities;
using Sub_Services.Execute.Excel;

public class TestForceRead
{
    public static async Task Main(string[] args)
    {
        // 1. Setup DI & DbContext
        var services = new ServiceCollection();
        services.AddDbContext<SNP_SubSystemDBContext>(options =>
            options.UseSqlServer("Server=snp-test;Database=SNP_SubSystem;Trusted_Connection=True;MultipleActiveResultSets=true;Encrypt=False"));
            
        // Mock necessary services for ExcelMonitorService...
        // Actually, it's easier to just run a web request using curl since the server is running on localhost:5093!
    }
}
