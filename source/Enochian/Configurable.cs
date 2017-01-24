using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Enochian
{
    using Config = IDictionary<string, object>;

    public interface IConfigurable
    {
        string AbsoluteFilePath { get; }
        IConfigurable Parent { get; set; }
        ICollection<IConfigurable> Children { get; }
        IEnumerable<ErrorRecord> Errors { get; }

        IConfigurable Configure(Config config);
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

        public ICollection<IConfigurable> Children
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

        public virtual IConfigurable Configure(Config config)
        {
            Id = config.Get<string>("Id", this);
            Description = config.Get<string>("Description", this);
            Changes = config.Get<string>("Changes", this);
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

        protected static TChild Load<TChild>(IConfigurable parent, string childPath)
            where TChild : IConfigurable, new()
        {
            return Load(parent, new TChild(), childPath);
        }

        protected static TChild Load<TChild>(IConfigurable parent, TChild child, string childPath)
            where TChild : IConfigurable
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

    public static class ConfigExtensions
    {
        public static T Get<T>(this Config config, string memberName, IConfigurable errorHandler)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (config.TryGetValue(memberName, out object value))
            {
                if (value is T)
                    return (T)value;
                if (errorHandler != null)
                    errorHandler.AddError(string.Format("config value '{0}' is not of type {1}", memberName, typeof(T).Name));
            }
            return default(T);
        }

        public static IEnumerable<IDictionary<string, object>> GetChildren(this Config config, string memberName, IConfigurable errorHandler)
        {
            return config.Get<IEnumerable<IDictionary<string, object>>>(memberName, errorHandler);
        }
    }
}
