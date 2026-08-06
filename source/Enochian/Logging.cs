global using Microsoft.Extensions.Logging;
global using System.Globalization;
global using System.Text.Json.Nodes;

namespace Enochian;

public static class Logging
{
    private static ILoggerFactory loggerFactory = LoggerFactory.Create(
        builder => builder.AddConsole());

    public static void Configure(ILoggerFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        loggerFactory = factory;
    }

    internal static ILogger CreateLogger<T>()
    {
        return loggerFactory.CreateLogger<T>();
    }
}
