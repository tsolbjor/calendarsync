using CalendarSync.Cli.Config;

namespace CalendarSync.Cli.Providers;

public class ProviderRegistry(IEnumerable<ICalendarProvider> providers)
{
    private readonly Dictionary<CalendarProvider, ICalendarProvider> _map =
        providers.ToDictionary(p => p.ProviderType);

    public ICalendarProvider Get(CalendarProvider type) => _map[type];
}
