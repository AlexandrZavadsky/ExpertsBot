using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;

namespace ExpertBot
{
    public class Program
    {
        public static void Main(string[] args)
        {
            IServiceCollection serviceCollection = new ServiceCollection();

            ConfigureServices(serviceCollection);

            IServiceProvider serviceProvider = serviceCollection.BuildServiceProvider();

            var proccessor = serviceProvider.GetService<BotProccessor>();

            Console.Title = proccessor.Bot.GetMeAsync().Result.Username;
            proccessor.Bot.StartReceiving();
            Console.WriteLine("Press any key to stop...");
            Console.ReadLine();
            proccessor.Bot.StopReceiving();
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            ILoggerFactory loggerFactory = new LoggerFactory()
                .AddConsole()
                .AddDebug();

            services.AddSingleton(loggerFactory); 
            services.AddLogging(); 

            IConfigurationRoot configuration = GetConfiguration();
            services.AddSingleton<IConfigurationRoot>(configuration);

            // Support typed Options
            services.AddOptions();
            services.Configure<BotOptions>(configuration.GetSection("BotOptions"));

            services.AddTransient<BotProccessor>();
        }

        private static IConfigurationRoot GetConfiguration()
        {
            return new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile($"appsettings.json", optional: true)
                .Build();
        }
    }
}
