using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Enochian.Flow
{
    public class Flow : Configurable
    {
        public Flow(string fname)
        {
            Load(fname);
        }

        protected override void Configure(dynamic config)
        {
            base.Configure((object)config);

            var featuresFname = Convert.ToString(config.Features);
        }
    }
}
