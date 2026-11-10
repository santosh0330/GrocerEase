using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace GrocerEase.IntegrationTests;

internal static class LogCapture
{
    /// <summary>
    /// The app with <paramref name="sink"/> added to its logging. With <paramref name="everything"/>, every category is logged at the
    /// lowest level, whatever <c>appsettings.json</c> says, so a test can search even the most detailed output.
    /// </summary>
    public static WebApplicationFactory<Program> WithCapturedLogs(
        this WebApplicationFactory<Program> factory, CapturingLogSink sink, bool everything = false)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(sink);

        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services => services.AddSingleton<ILogEventSink>(sink));
            if (everything)
            {
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                    new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Serilog:MinimumLevel:Default"] = "Verbose",
                        ["Serilog:MinimumLevel:Override:Microsoft"] = "Verbose",
                        ["Serilog:MinimumLevel:Override:System"] = "Verbose",
                    }));
            }
        });
    }
}
