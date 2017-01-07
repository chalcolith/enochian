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
    public interface IConfigurable
    {
        string AbsoluteFilePath { get; }
        IList<IConfigurable> Children { get; }
        IEnumerable<ErrorRecord> Errors { get; }

        void Configure(dynamic config);
        void AddError(string format, params object[] args);
        void AddError(int line, int column, string format, params object[] args);
    }

    public abstract class Configurable : IConfigurable
    {
        string absFilePath;
        IList<IConfigurable> children;
        IList<ErrorRecord> errors;

        public string AbsoluteFilePath
        {
            get { return absFilePath ?? "?"; }
            set { absFilePath = value; }
        }

        public string Id { get; set; }
        public string Description { get; set; }
        public string Changes { get; set; }

        public IList<IConfigurable> Children
        {
            get { return children ?? (children = new List<IConfigurable>()); }
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

        public void AddError(string format, params object[] args)
        {
            AddError(0, 0, format, args);
        }

        public void AddError(int line, int column, string format, params object[] args)
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

        public virtual void Configure(dynamic config)
        {
            Id = config.Id;
            Description = config.Description;
            Changes = config.Changes;
        }

        protected static void Load(string fname, IConfigurable obj)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));

            var serializer = new JsonSerializer
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                DateFormatHandling = DateFormatHandling.IsoDateFormat,
                DefaultValueHandling = DefaultValueHandling.Include,
                NullValueHandling = NullValueHandling.Ignore,                
            };
            serializer.Error += (object sender, ErrorEventArgs args) =>
            {
                if (args.ErrorContext.Error != null)
                {
                    obj.AddError("{0} ({1}): {2}", obj.AbsoluteFilePath, args.ErrorContext.Path, args.ErrorContext.Error);
                    args.ErrorContext.Handled = true;
                }
            };

            using (var stream = File.OpenRead(fname))
            using (var tr = new StreamReader(stream))
            using (var jr = new JsonTextReader(tr))
            {
                var config = serializer.Deserialize<ExpandoObject>(jr);
                obj.Configure(config);
            }
        }

        protected static T Load<T>(IConfigurable parent, string childPath)
            where T : IConfigurable, new()
        {
            if (string.IsNullOrWhiteSpace(childPath)) throw new ArgumentNullException(nameof(childPath));

            var child = new T();
            var absChildPath = !string.IsNullOrWhiteSpace(parent.AbsoluteFilePath)
                ? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(parent.AbsoluteFilePath), childPath))
                : Path.GetFullPath(childPath);
            Load(absChildPath, child);
            parent.Children.Add(child);
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
