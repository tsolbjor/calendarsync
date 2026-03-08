namespace CalendarSync.Cli.Config;

public class AppConfig
{
    public List<AccountConfig> Accounts { get; set; } = [];
    public List<SyncEntry> SyncedEvents { get; set; } = [];
}
