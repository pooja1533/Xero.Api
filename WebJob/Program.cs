using Microsoft.Azure.WebJobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SOARIntegration.Xero.Api.Accounts.Repository;
using SOARIntegration.Xero.Api.Accounts.Service;
using System;
using System.IO;
using System.Linq;
using Xero.Api.Accounts;

namespace SOARIntegration.Xero.Api.Accounts.WebJob
{
    class Program
    {
        static void Main(string[] args)
        {
            //Start-up
            var company = "";
            var syncMode = "-d";

            if (args.Length == 1)
            {
                company = args[0];
            }
            else if (args.Length == 2)
            {
                company = args[0];
                syncMode = args[1];
            }
            else
            {
                throw new Exception("Please specify a company indicator and (optionally) a sync mode (-f for a full sync, delta by default): Xero.Api.Accounts.exe <CompanyIndicator> <SyncMode> ");
            }
             

            var sharedFolder = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "SOAR.Shared");
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.org.{company}.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables();

            //Configure services
            IConfigurationRoot configuration = builder.Build();
            var serviceProvider = new ServiceCollection()
                .AddLogging(b => b.AddConsole()
                .AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning))
                .AddDbContext<Context>(options => options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")))
                .AddScoped(typeof(IRepository<>), typeof(Repository<>))                
                .AddSingleton<IAccountsService,AccountsService>()
                .BuildServiceProvider();

            var service = serviceProvider.GetService<IAccountsService>();
            var accountsLogger = serviceProvider.GetService<ILogger<AccountsModel>>();

            AccountsModel accountsModel = new AccountsModel(
                    accountsLogger, 
                    serviceProvider.GetService<Context>(),
                    service, company, configuration, syncMode);

            accountsModel.ProcessData();

            var importService = new ImportService(
                   serviceProvider.GetService<ILogger<ImportService>>(),
                   serviceProvider.GetService<Context>(), 
                   company, configuration, syncMode);

            importService.Import();
        }
    }       
}
