using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Enochian
{
    using Config = IDictionary<string, object>;

    public interface IErrorHandler
    {
        NLog.Logger Log { get; }
        IEnumerable<ErrorRecord> Errors { get; }
        IErrorHandler AddError(string format, params object[] args);
        IErrorHandler AddError(int line, int column, string format, params object[] args);
    }

    public interface IConfigurable : IErrorHandler
    {
        string AbsoluteFilePath { get; set; }
        IConfigurable Parent { get; set; }
        IEnumerable<IConfigurable> Children { get; }

        string Id { get; }
        string Description { get; }
        string Changes { get; }

        IConfigurable Configure(Config config);
    }

    public interface IFileReference
    {
        string RelativePath { get; }
    }

    public abstract class Configurable : IConfigurable
    {
        public const string CacheDir = ".enoch";

        string absFilePath;
        IList<ErrorRecord> errors;

        public Configurable(IConfigurable parent)
        {
            Parent = parent;
        }

        public string AbsoluteFilePath
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(absFilePath))
                    return absFilePath;
                if (Parent != null)
                    return Parent.AbsoluteFilePath;
                return "?";
            }
            set
            {
                absFilePath = value;
            }
        }

        public string Id { get; set; }
        public string Description { get; set; }
        public string Changes { get; set; }

        public IConfigurable Parent { get; set; }

        public virtual IEnumerable<IConfigurable> Children => Enumerable.Empty<IConfigurable>();

        public abstract NLog.Logger Log { get; }

        public IEnumerable<ErrorRecord> Errors
        {
            get
            {
                var allErrors = errors ?? Enumerable.Empty<ErrorRecord>();
                allErrors = allErrors.Concat(Children.SelectMany(child => child.Errors));
                return allErrors;
            }
        }

        public IErrorHandler AddError(string format, params object[] args)
        {
            AddError(0, 0, format, args);
            return this;
        }

        public IErrorHandler AddError(int line, int column, string format, params object[] args)
        {
            var message = string.Format("{0} for {1} '{2}'", string.Format(format, args), this.GetType().Name, Id ?? "?");
            Log.Error(message);

            if (errors == null) errors = new List<ErrorRecord>();
            errors.Add(new ErrorRecord
            {
                AbsoluteFilePath = AbsoluteFilePath,
                ErrorLine = line,
                ErrorColumn = column,
                Message = message,
            });
            return this;
        }

        public virtual IConfigurable Configure(Config config)
        {
            Id = config.Get<string>("id", this);
            Description = config.Get<string>("description", this);
            Changes = config.Get<string>("changes", this);
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
            serializer.Error += (object sender, Newtonsoft.Json.Serialization.ErrorEventArgs args) =>
            {
                if (args.ErrorContext.Error != null)
                {
                    obj.AddError("{0} ({1}): {2}", obj.AbsoluteFilePath, args.ErrorContext.Path, args.ErrorContext.Error);
                    args.ErrorContext.Handled = true;
                }
            };

            try
            {
                var path = Path.GetFullPath(fname);
                obj.AbsoluteFilePath = path;

                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (var tr = new StreamReader(fs))
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
            return child;
        }

        protected static string GetChildPath(string absParentPath, string childPath)
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
        public static T Get<T>(this Config config, string memberName, IErrorHandler errorHandler)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (config.TryGetValue(memberName, out object value) && value != null)
            {
                if (value is T)
                    return (T)value;

                if (value.GetType() == typeof(long))
                {
                    long n = (long)value;

                    if (typeof(T) == typeof(int))
                        return (T)(object)n;

                    if (typeof(T) == typeof(int?))
                        return (T)(object)(new int?((int)n));
                }

                try
                {
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    if (errorHandler != null)
                        errorHandler.AddError(string.Format("config value '{0}' is not of type {1}", memberName, typeof(T).Name));
                }
            }
            return default(T);
        }

        public static IEnumerable<T> GetList<T>(this Config config, string memberName, IErrorHandler errorHandler)
        {
            var list = config.Get<IEnumerable<object>>(memberName, errorHandler);
            return list?.OfType<T>() ?? Enumerable.Empty<T>();
        }

        public static IEnumerable<IDictionary<string, object>> GetChildren(this Config config, string memberName, IErrorHandler errorHandler)
        {
            return config.GetList<IDictionary<string, object>>(memberName, errorHandler);
        }
    }
}
