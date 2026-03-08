using System.Net.Http.Headers;
using System.Text;
using Ical.Net;
using Ical.Net.DataTypes;
using CalendarSync.Cli.Config;

namespace CalendarSync.Cli.Providers;

public class ReadOnlyCalendarProvider : ICalendarProvider
{
    public CalendarProvider ProviderType => CalendarProvider.ReadOnly;
    public bool IsReadOnly => true;

    public async Task LoginAsync(AccountConfig account, CancellationToken ct = default)
    {
        // Validate the URL is reachable and returns parseable iCal
        await FetchCalendarAsync(account, ct);
    }

    public Task LogoutAsync(AccountConfig account, CancellationToken ct = default)
        => Task.CompletedTask;

    public async Task<bool> IsAuthenticatedAsync(AccountConfig account, CancellationToken ct = default)
    {
        try
        {
            await FetchCalendarAsync(account, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(AccountConfig account, CancellationToken ct = default)
    {
        var calendar = await FetchCalendarAsync(account, ct);
        var name = calendar.Properties["X-WR-CALNAME"]?.Value?.ToString()
                   ?? account.DisplayName
                   ?? account.Id;
        return [new CalendarInfo(account.IcsUrl!, name, null)];
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(AccountConfig account, string calendarId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var calendar = await FetchCalendarAsync(account, ct);

        return calendar.Events
            .Where(e => e.DtStart is not null && e.DtEnd is not null)
            .Where(e => ToDateTimeOffset(e.DtStart!) < to && ToDateTimeOffset(e.DtEnd!) > from)
            .Select(e => new CalendarEvent(
                Id: e.Uid ?? Guid.NewGuid().ToString(),
                CalendarId: calendarId,
                Subject: e.Summary ?? "(No subject)",
                Start: ToDateTimeOffset(e.DtStart!),
                End: ToDateTimeOffset(e.DtEnd!),
                IsAllDay: e.IsAllDay,
                Location: e.Location,
                BodyPreview: e.Description is { Length: > 0 } d ? d[..Math.Min(200, d.Length)] : null))
            .ToList();
    }

    public Task<string> CreateEventAsync(AccountConfig account, string calendarId, NewEvent evt, CancellationToken ct = default)
        => throw new NotSupportedException("This is a read-only calendar feed.");

    public Task UpdateEventAsync(AccountConfig account, string calendarId, string eventId, NewEvent evt, CancellationToken ct = default)
        => throw new NotSupportedException("This is a read-only calendar feed.");

    public Task DeleteEventAsync(AccountConfig account, string calendarId, string eventId, CancellationToken ct = default)
        => throw new NotSupportedException("This is a read-only calendar feed.");

    private static async Task<Calendar> FetchCalendarAsync(AccountConfig account, CancellationToken ct)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CalendarSync/1.0");

        var response = await client.GetAsync(account.IcsUrl!, ct);
        response.EnsureSuccessStatusCode();

        var icsContent = await response.Content.ReadAsStringAsync(ct);

        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (contentType.Contains("html", StringComparison.OrdinalIgnoreCase)
            || icsContent.TrimStart().StartsWith("<", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The URL returned an HTML page instead of an ICS feed. " +
                "Make sure you are using the public ICS export URL, not a web calendar link.");
        }

        return Calendar.Load(icsContent)!;
    }

    private static DateTimeOffset ToDateTimeOffset(CalDateTime dt)
    {
        if (dt.IsUtc)
            return new DateTimeOffset(dt.Value, TimeSpan.Zero);
        return new DateTimeOffset(dt.Value, TimeZoneInfo.Local.GetUtcOffset(dt.Value));
    }
}
