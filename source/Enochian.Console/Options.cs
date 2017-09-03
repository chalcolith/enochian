using System;
using System.Collections.Generic;
using System.Text;
using CommandLine;

namespace Enochian.Console
{
    class Options
    {
        [Value(0, Required = true, HelpText = "Flow configuration file.")]
        public string ConfigFile { get; set; }
    }
}
