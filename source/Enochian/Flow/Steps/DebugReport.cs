using System;
using System.Collections.Generic;
using System.Text;

namespace Enochian.Flow.Steps
{
    public class DebugReport : FlowStep
    {
        public DebugReport(FlowContainer parent, FlowStep previous, dynamic config)
            : base(null, null, parent, previous, (object)config)
        {
        }

        public override void Configure(dynamic config)
        {
            base.Configure((object)config);
        }
    }
}
