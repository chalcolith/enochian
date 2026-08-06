using System.Text.Json;

namespace Enochian;

public interface IErrorHandler
{
    ILogger Log { get; }
    IEnumerable<ErrorRecord> Errors { get; }
    IErrorHandler AddError(string format, params object?[] args);
    IErrorHandler AddError(int line, int column, string format, params object?[] args);
}

public interface IConfigurable : IErrorHandler
{
    string AbsoluteFilePath { get; set; }
    IConfigurable? Parent { get; set; }
    IEnumerable<IConfigurable> Children { get; }

    string? Id { get; }
    string? Description { get; }
    string? Changes { get; }

    IConfigurable Configure(JsonObject config);
    void PostConfigure();
}

public abstract class Configurable(IConfigurable? parent) : IConfigurable
{
    public const string CacheDir = ".enoch";
    private static readonly JsonDocumentOptions ConfigDocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };
    private IList<ErrorRecord>? errors;

    public string AbsoluteFilePath
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(field))
            {
                return field;
            }

            if (Parent != null)
            {
                return Parent.AbsoluteFilePath;
            }

            return "?";
        }
        set;
    }

    public string? Id { get; set; }
    public string? Description { get; set; }
    public string? Changes { get; set; }

    public IConfigurable? Parent { get; set; } = parent;

    public virtual IEnumerable<IConfigurable> Children => [];

    public abstract ILogger Log { get; }

    public IEnumerable<ErrorRecord> Errors
    {
        get
        {
            var allErrors = errors ?? Enumerable.Empty<ErrorRecord>();
            allErrors = allErrors.Concat(Children.SelectMany(child => child.Errors));
            return allErrors;
        }
    }

    public IErrorHandler AddError(string format, params object?[] args)
    {
        _ = AddError(0, 0, format, args);
        return this;
    }

    public IErrorHandler AddError(int line, int column, string format, params object?[] args)
    {
        var detail = string.Format(CultureInfo.InvariantCulture, format, args);
        var message = string.Format(CultureInfo.InvariantCulture, "{0} for {1} '{2}'", detail, GetType().Name, Id ?? "?");
        Log.LogError("{Message}", message);

        errors ??= [];

        errors.Add(new ErrorRecord
        {
            AbsoluteFilePath = AbsoluteFilePath,
            ErrorLine = line,
            ErrorColumn = column,
            Message = message,
        });
        return this;
    }

    public virtual IConfigurable Configure(JsonObject config)
    {
        Id = config.Get<string>("id", this);
        Description = config.Get<string>("description", this);
        Changes = config.Get<string>("changes", this);
        return this;
    }

    public virtual void PostConfigure()
    {
    }

    protected static IConfigurable Load(string fname, IConfigurable obj)
    {
        ArgumentNullException.ThrowIfNull(obj);

        try
        {
            var path = Path.GetFullPath(fname);
            obj.AbsoluteFilePath = path;
            using var stream = File.OpenRead(path);
            var config = JsonNode.Parse(stream, documentOptions: ConfigDocumentOptions)?.AsObject()
                ?? throw new JsonException("The configuration file does not contain a JSON object.");
            _ = obj.Configure(config);
        }
        catch (Exception e)
        {
            _ = obj.AddError("error loading: {0}", e.Message);
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
        if (string.IsNullOrWhiteSpace(childPath))
        {
            throw new ArgumentNullException(nameof(childPath));
        }

        var absChildPath = GetChildPath(parent.AbsoluteFilePath, childPath);
        _ = Load(absChildPath, child);
        child.Parent = parent;
        return child;
    }

    protected static string GetChildPath(string absParentPath, string childPath)
    {
        var absChildPath = !string.IsNullOrWhiteSpace(absParentPath)
            ? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(absParentPath) ?? ".", childPath))
            : Path.GetFullPath(childPath);
        return absChildPath;
    }
}

public abstract class RelativeConfigurable(IConfigurable? parent) : Configurable(parent)
{
    public string? RelativePath { get; internal set; }
}

public class ErrorRecord
{
    public string? AbsoluteFilePath { get; set; }
    public int ErrorLine { get; set; }
    public int ErrorColumn { get; set; }
    public string? Message { get; set; }
}

public static class ConfigExtensions
{
    public static T? Get<T>(this JsonObject config, string memberName, IErrorHandler errorHandler)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!config.TryGetPropertyValue(memberName, out var value) || value == null)
        {
            return default;
        }

        try
        {
            return value.Deserialize<T>();
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
        {
            _ = errorHandler.AddError("config value '{0}' is not of type {1}", memberName, typeof(T).Name);
            return default;
        }
    }

    public static IEnumerable<T> GetList<T>(this JsonObject config, string memberName, IErrorHandler errorHandler)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!config.TryGetPropertyValue(memberName, out var value) || value == null)
        {
            return [];
        }

        if (value is not JsonArray array)
        {
            _ = errorHandler.AddError("config value '{0}' is not an array", memberName);
            return [];
        }

        var result = new List<T>();
        foreach (var element in array)
        {
            if (element == null)
            {
                continue;
            }

            try
            {
                var item = element.Deserialize<T>();
                if (item != null)
                {
                    result.Add(item);
                }
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException)
            {
                _ = errorHandler.AddError("config array '{0}' contains a value that is not of type {1}", memberName, typeof(T).Name);
            }
        }

        return result;
    }

    public static IEnumerable<JsonObject> GetChildren(this JsonObject config, string memberName, IErrorHandler errorHandler)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (!config.TryGetPropertyValue(memberName, out var value) || value == null)
        {
            return [];
        }

        if (value is not JsonArray array)
        {
            _ = errorHandler.AddError("config value '{0}' is not an array", memberName);
            return [];
        }

        return array.OfType<JsonObject>();
    }
}
