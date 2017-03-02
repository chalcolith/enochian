using Enochian.Text;
using System;
using System.Collections.Generic;
using System.Text;

namespace Enochian.Flow.Steps
{
    public class DTWMatcher : FlowStep<TextChunk, TextChunk>
    {
        public DTWMatcher(IConfigurable parent, IFlowResources resources)
            : base(parent, resources)
        {
        }

        public DTWMatcher(IConfigurable parent, IFlowResources resources, FlowContainer container, FlowStep previous, IDictionary<string, object> config)
            : base(parent, resources, container, previous, config)
        {
        }


    }
}
