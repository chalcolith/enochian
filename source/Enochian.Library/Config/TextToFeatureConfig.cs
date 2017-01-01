using System;
using System.Collections.Generic;
using System.Text;

namespace Enochian.Config
{
    public class TextToFeatureConfig : ConfigEntity
    {
    }

    public class TextToFeaturePattern
    {
        public string Text { get; set; }
        public IList<string> Features { get; set; }
    }
}
