using System.Text.Json;
using System.Text.Json.Serialization;

namespace CalendarSync.Cli.Config;

public static class ConfigManager
{
    private static readonly string ConfigDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".calendarsync");

    private static readonly string ConfigFile = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    public static AppConfig Load()
    {
        if (!File.Exists(ConfigFile))
            return new AppConfig();

        var json = File.ReadAllText(ConfigFile);

        // One-shot migration: v1 had "Tenants" (Microsoft only), v2 uses "Accounts"
        if (json.Contains("\"Tenants\"") && !json.Contains("\"Accounts\""))
        {
            var v1 = JsonSerializer.Deserialize<V1AppConfig>(json, JsonOptions);
            if (v1?.Tenants is { Count: > 0 })
            {
                var migrated = new AppConfig
                {
                    Accounts = v1.Tenants.Select(t => new AccountConfig
                    {
                        Id = t.Id,
                        Provider = CalendarProvider.Microsoft,
                        DisplayName = t.DisplayName,
                        TenantId = t.TenantId,
                        ClientId = t.ClientId,
                    }).ToList()
                };
                Save(migrated);
                return migrated;
            }
        }

        return JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
    }

    public static void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(ConfigFile, JsonSerializer.Serialize(config, JsonOptions));

        // Restrict config file to owner-only on Unix (contains credentials)
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(ConfigFile,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    /// <summary>
    /// Directory where OAuth token caches are stored. Created with owner-only permissions
    /// on Unix so that all files inside are inaccessible to other users.
    /// </summary>
    public static string TokenCacheDir
    {
        get
        {
            var dir = Path.Combine(ConfigDir, "tokens");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                // Restrict the directory to owner-only on Unix so token files inside
                // (written by Google.Apis FileDataStore and MSAL) are not world-readable.
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            return dir;
        }
    }

    public static string ConfigDirectory => ConfigDir;

    // Migration support: v1 config shape
    private class V1AppConfig
    {
        public List<V1TenantConfig> Tenants { get; set; } = [];
    }
    private class V1TenantConfig
    {
        public string Id { get; set; } = "";
        public string TenantId { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string? DisplayName { get; set; }
    }
}
