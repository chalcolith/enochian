using System;
using System.Collections.Generic;
using System.Text;

namespace Enochian.Flow.Steps
{
    public class DebugReport : FlowStep
    {
        public DebugReport(FlowContainer parent, FlowStep previous)
            : base(parent, previous)
        {
        }

        protected override bool Process(object input, out object output)
        {
            output = null;
            return true;
        }
    }

    public class DebugReportConfig : Config.FlowConfig
    {
        
    }
}
