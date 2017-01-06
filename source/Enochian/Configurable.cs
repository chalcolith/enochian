using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Enochian
{
    public abstract class Configurable
    {
        string absFilePath;
        IList<Configurable> children;
        IList<ErrorRecord> errors;

        public string AbsoluteFilePath
        {
            get { return absFilePath ?? "?"; }
            set { absFilePath = value; }
        }

        public string Id { get; set; }
        public string Description { get; set; }
        public string Changes { get; set; }

        protected IList<Configurable> Children
        {
            get { return children ?? (children = new List<Configurable>()); }
        }

        public IEnumerable<ErrorRecord> Errors
        {
            get
            {
                var allErrors = errors ?? Enumerable.Empty<ErrorRecord>();
                if (children != null)
                    allErrors = allErrors.Concat(children.SelectMany(child => child.Errors));
                return allErrors;
            }
        }

        protected void AddError(string format, params object[] args)
        {
            AddError(0, 0, format, args);
        }

        protected void AddError(int line, int column, string format, params object[] args)
        {
            if (errors == null) errors = new List<ErrorRecord>();
            errors.Add(new ErrorRecord
            {
                AbsoluteFilePath = AbsoluteFilePath,
                ErrorLine = line,
                ErrorColumn = column,
                Message = string.Format(format, args),
            });
        }

        protected virtual void Configure(dynamic config)
        {
            Id = config.Id;
            Description = config.Description;
            Changes = config.Changes;
        }

        protected void Load(string fname)
        {
            var serializer = new JsonSerializer
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                DateFormatHandling = DateFormatHandling.IsoDateFormat,
                DefaultValueHandling = DefaultValueHandling.Include,
                NullValueHandling = NullValueHandling.Ignore,
            };

            using (var stream = File.OpenRead(fname))
            using (var tr = new StreamReader(stream))
            using (var jr = new JsonTextReader(tr))
            {
                var config = serializer.Deserialize<ExpandoObject>(jr);
                Configure(config);
            }
        }

        protected T Load<T>(string parentPath, string childPath)
            where T : Configurable
        {
            var child = Activator.CreateInstance<T>();
            var absChildPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(parentPath), childPath));
            child.Load(absChildPath);
            return child;
        }
    }

    public class ErrorRecord
    {
        public string AbsoluteFilePath { get; set; }
        public int ErrorLine { get; set; }
        public int ErrorColumn { get; set; }
        public string Message { get; set; }
    }
}
