namespace Enochian.Console;

internal sealed class Options
{
    public const string Usage = "Usage: Enochian.Console config.json [--logFile my.log] [--overrides assignments]";

    public required string ConfigFile { get; init; }

    public string? LogFile { get; init; }

    public string? Overrides { get; init; }

    public static Options Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? configFile = null;
        string? logFile = null;
        string? overrides = null;

        for (int index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                if (configFile != null)
                {
                    throw new ArgumentException(Usage, nameof(args));
                }

                configFile = argument;
                continue;
            }

            var separatorIndex = argument.IndexOf('=');
            var name = separatorIndex >= 0 ? argument[..separatorIndex] : argument;
            var value = separatorIndex >= 0
                ? argument[(separatorIndex + 1)..]
                : index + 1 < args.Length ? args[++index] : null;

            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentException(Usage, nameof(args));
            }

            if (name.Equals("--logFile", StringComparison.OrdinalIgnoreCase))
            {
                logFile = value;
            }
            else if (name.Equals("--overrides", StringComparison.OrdinalIgnoreCase))
            {
                overrides = value;
            }
            else
            {
                throw new ArgumentException(Usage, nameof(args));
            }
        }

        return new Options
        {
            ConfigFile = configFile ?? throw new ArgumentException(Usage, nameof(args)),
            LogFile = logFile,
            Overrides = overrides,
        };
    }
}
