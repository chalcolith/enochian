using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using Enochian;
using Enochian.Console;
using Enochian.Flow;
using Enochian.Flow.Steps;

class Program
{
    static NLog.Logger logger;
    static NLog.Logger Log { get => logger ?? (logger = NLog.LogManager.GetCurrentClassLogger()); }

    static int Main(string[] args)
    {
        try
        {
            var options = GetOptions(args);
            ConfigureLogging(options);

            var configFilePath = options.ConfigFile;
            if (string.IsNullOrWhiteSpace(configFilePath)
                || !File.Exists(configFilePath = Path.GetFullPath(configFilePath)))
            {
                throw new Exception("Config file '" + configFilePath + "' not found.");
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
            Log.Error(e.Message);
#if DEBUG
            Console.Error.WriteLine(e);
#else
            Console.Error.WriteLine(e.Message);
#endif
            return 1;
        }
    }

    static Options GetOptions(string[] args)
    {
        Options options;
        var parseResult = CommandLine.Parser.Default.ParseArguments<Options>(args);
        switch (parseResult)
        {
            case CommandLine.Parsed<Options> parsed:
                options = parsed.Value;
                break;
            default:
                throw new Exception(Options.Usage);
        }

        return options;
    }

    static void ConfigureLogging(Options options)
    {
        var config = new NLog.Config.LoggingConfiguration();

        var console = new NLog.Targets.ColoredConsoleTarget
        {
            Layout = @"${logger} ${message}",
        };
        config.AddTarget("console", console);
        config.LoggingRules.Add(new NLog.Config.LoggingRule("*", NLog.LogLevel.Info, console));

        if (!string.IsNullOrWhiteSpace(options.LogFile))
        {
            var file = new NLog.Targets.FileTarget
            {
                FileName = "${basedir}/" + options.LogFile,
                Layout = @"${date:format=HH\:mm\:ss} ${logger} ${message}",
            };
            config.AddTarget("file", file);
            config.LoggingRules.Add(new NLog.Config.LoggingRule("*", NLog.LogLevel.Info, file));
        }

        NLog.LogManager.Configuration = config;
    }

    static void HandleErrors(IErrorHandler obj)
    {
        var sb = new StringBuilder();
        foreach (var error in obj.Errors)
        {
            sb.AppendFormat("{0}:{1}: {2}", error.AbsoluteFilePath, error.ErrorLine, error.Message);
            sb.AppendLine();
        }
        if (sb.Length > 0)
        {
            throw new Exception(sb.ToString());
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
                IConfigurable cur = flow;
                foreach (var token in nameTokens.Take(nameTokens.Length - 1))
                {
                    cur = cur.Children.FirstOrDefault(child => child.Id.Equals(token, StringComparison.InvariantCultureIgnoreCase));
                    if (cur == null)
                    {
                        flow.AddError("Unknown config object with Id '{0}'", token);
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
                            flow.AddError("Unable to set field value for field '{0}' of type '{1}'", nameAndValue[0], propInfo.PropertyType.Name);
                        }
                    }
                    else
                    {
                        flow.AddError("Unknown config field '{0}'", propName);
                    }
                }
            }
            else
            {
                flow.AddError("Invalid config override '{0}'", nameAndValue);
            }
        }
    }
}
