using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace Enochian.Config
{
    public abstract class ConfigEntity
    {
        IList<string> errors;

        public string Id { get; set; }
        public string Description { get; set; }
        public string Changes { get; set; }

        [JsonIgnore]
        public string AbsolutePath { get; internal set; }

        [JsonIgnore]
        public bool HasErrors { get { return errors != null && errors.Count > 0; } }

        [JsonIgnore]
        public IList<string> Errors
        {
            get { return errors ?? (errors = new List<string>()); }
        }
    }
}
