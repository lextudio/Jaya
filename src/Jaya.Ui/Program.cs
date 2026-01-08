//
// Copyright (c) Rubal Walia. All rights reserved.
// Licensed under the 3-Clause BSD license. See LICENSE file in the project root for full license information.
//
using Avalonia;
using Jaya.Ui.Logging;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;
using System;

namespace Jaya.Ui
{
    public class Program
    {
        // This method is needed for IDE previewer infrastructure
        static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                           .UsePlatformDetect()
                           .LogToMySink();
        }

        // The entry point. Things aren't ready yet, so at this point
        // you shouldn't use any Avalonia types or anything that expects
        // a SynchronizationContext to be ready
        static void Main(string[] args)
        {
            var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? 
                #if DEBUG
                "Development";
                #else
                "Production";
                #endif

            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddJsonFile($"appsettings.{env}.json", optional: true, reloadOnChange: false)
                .AddEnvironmentVariables()
                .Build();

            var logConfig = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .Enrich.FromLogContext();

            if (configuration.GetValue<bool>("Logging:ExcludeAvaloniaCategory"))
            {
                logConfig = logConfig.Filter.ByExcluding(e =>
                    e.Properties.TryGetValue("Category", out var category) &&
                    category is ScalarValue scalar &&
                    string.Equals(scalar.Value?.ToString(), "Avalonia", StringComparison.OrdinalIgnoreCase));
            }

            Log.Logger = logConfig.CreateLogger();

            Log.Information("Starting Jaya...");

            try
            {
                System.Diagnostics.Trace.Listeners.Add(new Logging.AvaloniaTraceSerilogSink());
            }
            catch { }

            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application terminated unexpectedly");
                throw;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}
