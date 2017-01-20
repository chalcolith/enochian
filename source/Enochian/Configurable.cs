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
        IConfigurable Parent { get; set; }
        IList<IConfigurable> Children { get; }
        IEnumerable<ErrorRecord> Errors { get; }

        IConfigurable Configure(dynamic config);
        IConfigurable AddError(string format, params object[] args);
        IConfigurable AddError(int line, int column, string format, params object[] args);
    }

    public interface ILoadedFromFile
    {
        string Name { get; }
        string Path { get; }
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

        public IConfigurable Parent { get; set; }

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

        public IConfigurable AddError(string format, params object[] args)
        {
            AddError(0, 0, format, args);
            return this;
        }

        public IConfigurable AddError(int line, int column, string format, params object[] args)
        {
            if (errors == null) errors = new List<ErrorRecord>();
            errors.Add(new ErrorRecord
            {
                AbsoluteFilePath = AbsoluteFilePath,
                ErrorLine = line,
                ErrorColumn = column,
                Message = string.Format(format, args),
            });
            return this;
        }

        public virtual IConfigurable Configure(dynamic config)
        {
            Id = config.Id;
            Description = config.Description;
            Changes = config.Changes;
            return this;
        }

        protected static IConfigurable Load(string fname, IConfigurable obj)
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

            try
            {
                using (var stream = File.OpenRead(fname))
                using (var tr = new StreamReader(stream))
                using (var jr = new JsonTextReader(tr))
                {
                    var config = serializer.Deserialize<ExpandoObject>(jr);
                    obj.Configure(config);
                }
            }
            catch (Exception e)
            {
                obj.AddError("error loading: {0}", e.Message);
            }

            return obj;
        }

        protected static T Load<T>(IConfigurable parent, string childPath)
            where T : IConfigurable, new()
        {
            return Load(parent, new T(), childPath);
        }

        protected static T Load<T>(IConfigurable parent, T child, string childPath)
            where T : IConfigurable
        {
            if (string.IsNullOrWhiteSpace(childPath)) throw new ArgumentNullException(nameof(childPath));

            var absChildPath = GetChildPath(parent.AbsoluteFilePath, childPath);
            Load(absChildPath, child);
            child.Parent = parent;
            parent.Children.Add(child);
            return child;
        }

        static string GetChildPath(string absParentPath, string childPath)
        {
            var absChildPath = !string.IsNullOrWhiteSpace(absParentPath)
                ? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(absParentPath), childPath))
                : Path.GetFullPath(childPath);
            return absChildPath;
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
