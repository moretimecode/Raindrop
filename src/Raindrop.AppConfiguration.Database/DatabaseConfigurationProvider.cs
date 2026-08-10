using System.Data;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Raindrop.AppConfiguration.Database;

public class DatabaseConfigurationProvider : ConfigurationProvider, IDisposable
{
    private readonly ILogger _logger;
    private readonly DatabaseConfigOptions _options;
    private readonly Timer? _refreshTimer;
    private readonly object _lock = new();
    private Dictionary<string, string?> _previousData = new(StringComparer.OrdinalIgnoreCase);

    public DatabaseConfigurationProvider(DatabaseConfigOptions options, ILoggerFactory? loggerFactory = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (options.BuildConnection is null)
            throw new ArgumentException("BuildConnection cannot be null.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.Sql))
            throw new ArgumentException("Sql cannot be null or empty.", nameof(options));

        _logger = loggerFactory?.CreateLogger<DatabaseConfigurationProvider>()
                  ?? NullLoggerFactory.Instance.CreateLogger<DatabaseConfigurationProvider>();

        Load();

        if (_options.ReloadOnChange)
        {
            _refreshTimer = new Timer(OnTimerElapsed, null, _options.RefreshInterval, _options.RefreshInterval);
        }
    }

    public override void Load()
    {
        try
        {
            var newData = ReadFromDatabase();

            lock (_lock)
            {
                Data = newData;
                _previousData = new Dictionary<string, string?>(newData, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            HandleLoadFailure(ex, isInitialLoad: true);
        }
    }

    private Dictionary<string, string?> ReadFromDatabase()
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        using var conn = _options.BuildConnection();
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = _options.Sql;

        using var reader = cmd.ExecuteReader();

        var columnNames = new string[reader.FieldCount];
        for (var i = 0; i < reader.FieldCount; i++)
        {
            columnNames[i] = _options.UsePascalNaming
                ? SnakeToPascal(reader.GetName(i))
                : reader.GetName(i);
        }

        var rowIndex = 0;
        while (reader.Read())
        {
            for (var col = 0; col < reader.FieldCount; col++)
            {
                var key = $"{_options.SectionName}:{rowIndex}:{columnNames[col]}";
                var rawValue = reader.IsDBNull(col) ? null : reader.GetValue(col)?.ToString();

                if (rawValue is null)
                {
                    data[key] = null;
                }
                else
                {
                    ExpandJsonCell(data, key, rawValue);
                }
            }

            rowIndex++;
        }

        return data;
    }

    private void ExpandJsonCell(Dictionary<string, string?> data, string key, string value)
    {
        value = value.Trim();
        if ((value.StartsWith('{') && value.EndsWith('}')) ||
            (value.StartsWith('[') && value.EndsWith(']')))
        {
            try
            {
                using var doc = JsonDocument.Parse(value);
                ExpandJsonElement(data, key, doc.RootElement);
                return;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "数据库配置 key '{Key}' 的值看起来像 JSON，但解析失败，按纯文本处理。", key);

                if (_options.StrictJsonParsing)
                    throw;
            }
        }

        data[key] = value;
    }

    private static void ExpandJsonElement(Dictionary<string, string?> data, string key, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    ExpandJsonElement(data, $"{key}:{prop.Name}", prop.Value);
                }
                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    ExpandJsonElement(data, $"{key}:{index}", item);
                    index++;
                }
                break;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                data[key] = null;
                break;

            default:
                data[key] = element.ToString();
                break;
        }
    }

    private static string SnakeToPascal(string name)
    {
        if (!name.Contains('_'))
            return name;

        var sb = new StringBuilder(name.Length);
        var capitalize = true;

        foreach (var c in name)
        {
            if (c == '_')
            {
                capitalize = true;
                continue;
            }

            sb.Append(capitalize ? char.ToUpperInvariant(c) : c);
            capitalize = false;
        }

        return sb.ToString();
    }

    private void OnTimerElapsed(object? state)
    {
        try
        {
            var newData = ReadFromDatabase();

            lock (_lock)
            {
                if (DataEquals(_previousData, newData))
                    return;

                Data = newData;
                _previousData = new Dictionary<string, string?>(newData, StringComparer.OrdinalIgnoreCase);
            }

            OnReload();
        }
        catch (Exception ex)
        {
            HandleLoadFailure(ex, isInitialLoad: false);
        }
    }

    private void HandleLoadFailure(Exception exception, bool isInitialLoad)
    {
        var behavior = isInitialLoad
            ? _options.InitialLoadFailureBehavior
            : _options.ReloadFailureBehavior;

        var operation = isInitialLoad ? "加载" : "刷新";
        _logger.LogError(exception, "数据库配置{Operation}失败。", operation);
        NotifyLoadException(exception);

        if (behavior == ConfigurationLoadFailureBehavior.Throw)
        {
            throw new InvalidOperationException($"数据库配置{operation}失败。", exception);
        }
    }

    private void NotifyLoadException(Exception exception)
    {
        try
        {
            _options.OnLoadException?.Invoke(exception);
        }
        catch (Exception notificationException)
        {
            _logger.LogError(notificationException, "数据库配置加载失败通知回调异常。");
        }
    }

    private static bool DataEquals(IDictionary<string, string?>? a, IDictionary<string, string?>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;
        if (a.Count != b.Count) return false;

        foreach (var kv in a)
        {
            if (!b.TryGetValue(kv.Key, out var val) || val != kv.Value)
                return false;
        }

        return true;
    }

    public void Dispose()
    {
        _refreshTimer?.Dispose();
    }
}
