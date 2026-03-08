using CalendarSync.Cli.Config;

namespace CalendarSync.Cli.Providers;

public interface ICalendarProvider
{
    CalendarProvider ProviderType { get; }

    /// <summary>
    /// When true, this account can only be used as an event source.
    /// Creating placeholder events in it is not supported.
    /// </summary>
    bool IsReadOnly { get; }

    Task LoginAsync(AccountConfig account, CancellationToken ct = default);
    Task LogoutAsync(AccountConfig account, CancellationToken ct = default);
    Task<bool> IsAuthenticatedAsync(AccountConfig account, CancellationToken ct = default);

    Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(AccountConfig account, CancellationToken ct = default);
    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(AccountConfig account, string calendarId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default);

    Task<string> CreateEventAsync(AccountConfig account, string calendarId, NewEvent evt, CancellationToken ct = default);
    Task UpdateEventAsync(AccountConfig account, string calendarId, string eventId, NewEvent evt, CancellationToken ct = default);
    Task DeleteEventAsync(AccountConfig account, string calendarId, string eventId, CancellationToken ct = default);
}

public record CalendarInfo(string Id, string Name, string? Color);

public record CalendarEvent(
    string Id,
    string CalendarId,
    string Subject,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string? BodyPreview);

/// <summary>Payload for creating or updating a placeholder event.</summary>
public record NewEvent(
    string Subject,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Body);
