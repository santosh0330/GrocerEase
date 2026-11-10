using Microsoft.Extensions.Logging;

namespace GrocerEase.IntegrationTests;

internal static partial class ShutdownLogMessages
{
    [LoggerMessage(Level = LogLevel.Information, Message = "last words {Canary}")]
    public static partial void LastWords(ILogger logger, string canary);
}
