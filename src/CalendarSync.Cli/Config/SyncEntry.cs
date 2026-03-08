namespace CalendarSync.Cli.Config;

/// <summary>
/// Tracks a placeholder event that was created in a target calendar from a source event.
/// Used to update or remove the placeholder on subsequent plan runs.
/// </summary>
public class SyncEntry
{
    public required string SourceAccountId { get; set; }
    public required string SourceCalendarId { get; set; }
    public required string SourceEventId { get; set; }

    public required string TargetAccountId { get; set; }
    public required string TargetCalendarId { get; set; }
    public required string TargetEventId { get; set; }

    /// <summary>Start time of the source event at the time it was last synced.</summary>
    public DateTimeOffset? SyncedStart { get; set; }
    /// <summary>End time of the source event at the time it was last synced.</summary>
    public DateTimeOffset? SyncedEnd { get; set; }
    /// <summary>Whether the source event was all-day at the time it was last synced.</summary>
    public bool? SyncedIsAllDay { get; set; }
}
