using System;
using System.Collections.Generic;
using System.Text;
using CommandLine;

namespace Enochian.Console
{
    class Options
    {
        public const string Usage = "Usage: Enochian.Console config.json [--logFile my.log]";

        [Value(0, Required = true, HelpText = "Flow configuration file.")]
        public string ConfigFile { get; set; }

        [Option(HelpText = "Log file.")]
        public string LogFile { get; set; }
    }
}
