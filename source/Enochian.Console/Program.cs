using System;
using System.IO;
using System.Linq;
using System.Text;
using Enochian;
using Enochian.Console;
using Enochian.Flow;

class Program
{
    static int Main(string[] args)
    {
        try
        {
            Options options;
            var parseResult = CommandLine.Parser.Default.ParseArguments<Options>(args);
            switch (parseResult)
            {
                case CommandLine.Parsed<Options> parsed:
                    options = parsed.Value;
                    break;
                default:
                    throw new Exception("Usage: Enochian.Console config.json");
            }

            var configFilePath = options.ConfigFile;
            if (string.IsNullOrWhiteSpace(configFilePath)
                || !File.Exists(configFilePath = Path.GetFullPath(configFilePath)))
            {
                throw new Exception("Config file '" + configFilePath + "' not found.");
            }

            var flow = new Flow(configFilePath);
            HandleErrors(flow);

            var outputs = flow.GetOutputs().ToList();
            HandleErrors(flow);

            return 0;
        }
        catch (Exception e)
        {
#if DEBUG
            Console.Error.WriteLine(e);
#else
            Console.Error.WriteLine(e.Message);
#endif
            return 1;
        }
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
}
