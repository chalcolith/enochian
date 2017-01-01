using System;
using System.Collections.Generic;
using System.Text;

namespace Enochian.Config
{
    public class FlowConfig : ConfigEntity
    {
        public IDictionary<string, string> Properties { get; set; }
    }
}
