using Enochian;
using Enochian.Console;
using Enochian.Flow;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Reflection;
using System.Text;

using var loggerFactory = LoggerFactory.Create(builder => builder.AddSimpleConsole(options => options.SingleLine = true));
var logger = loggerFactory.CreateLogger("Enochian.Console");

try
{
    var options = GetOptions(args);
    ConfigureLogging(options, loggerFactory);

    var configFilePath = options.ConfigFile;
    if (string.IsNullOrWhiteSpace(configFilePath)
        || !File.Exists(configFilePath = Path.GetFullPath(configFilePath)))
    {
        throw new FileNotFoundException($"Config file '{configFilePath}' not found.", configFilePath);
    }

    var flow = new Flow(configFilePath);
    HandleErrors(flow);

    var overrides = options.Overrides;
    if (!string.IsNullOrWhiteSpace(overrides))
    {
        ApplyOverrides(flow, overrides);
        HandleErrors(flow);
    }

    flow.ProcessAll();
    HandleErrors(flow);

    return 0;
}
catch (Exception e)
{
    logger.LogError("{Message}", e.Message);
#if DEBUG
    Console.Error.WriteLine(e);
#else
    Console.Error.WriteLine(e.Message);
#endif
    return 1;
}

static Options GetOptions(string[] args)
{
    return Options.Parse(args);
}

static void ConfigureLogging(Options options, ILoggerFactory loggerFactory)
{
    if (!string.IsNullOrWhiteSpace(options.LogFile))
    {
        loggerFactory.AddProvider(new FileLoggerProvider(Path.Combine(AppContext.BaseDirectory, options.LogFile)));
    }

    Logging.Configure(loggerFactory);
}

static void HandleErrors(Flow flow)
{
    var sb = new StringBuilder();
    foreach (var error in flow.Errors)
    {
        _ = sb.AppendFormat(CultureInfo.InvariantCulture, "{0}:{1}: {2}", error.AbsoluteFilePath, error.ErrorLine, error.Message);
        _ = sb.AppendLine();
    }
    if (sb.Length > 0)
    {
        throw new InvalidOperationException(sb.ToString());
    }
}

static void ApplyOverrides(Flow flow, string overrides)
{
    var assignments = overrides.Split('|');
    foreach (var assignment in assignments)
    {
        var nameAndValue = assignment.Split('=');
        if (nameAndValue.Length == 2)
        {
            var nameTokens = nameAndValue[0].Split('/');
            IConfigurable? cur = flow;
            foreach (var token in nameTokens.Take(nameTokens.Length - 1))
            {
                cur = cur.Children.FirstOrDefault(child => string.Equals(child.Id, token, StringComparison.OrdinalIgnoreCase));
                if (cur == null)
                {
                    _ = flow.AddError("Unknown config object with Id '{0}'", token);
                    break;
                }
            }
            if (cur != null)
            {
                var propName = nameTokens.Last();
                var propInfo = cur.GetType().GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (propInfo != null)
                {
                    if (propInfo.PropertyType.IsAssignableFrom(typeof(string)))
                    {
                        propInfo.SetValue(cur, nameAndValue[1]);
                    }
                    else if (propInfo.PropertyType.IsAssignableFrom(typeof(string[])))
                    {
                        propInfo.SetValue(cur, nameAndValue[1].Split(','));
                    }
                    else
                    {
                        _ = flow.AddError("Unable to set field value for field '{0}' of type '{1}'", nameAndValue[0], propInfo.PropertyType.Name);
                    }
                }
                else
                {
                    _ = flow.AddError("Unknown config field '{0}'", propName);
                }
            }
        }
        else
        {
            _ = flow.AddError("Invalid config override '{0}'", nameAndValue);
        }
    }
}
