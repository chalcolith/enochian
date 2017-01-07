using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Text;
using Enochian.Encoding;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Enochian.Flow
{
    public class Flow : Configurable
    {
        public Flow(string fname)
        {
            Load(fname, this);
        }

        public Features Features { get; private set; }

        public override void Configure(dynamic config)
        {
            base.Configure((object)config);

            var featuresPath = Convert.ToString(config.Features);
            Features = Load<Features>(this, featuresPath);
        }
    }
}
