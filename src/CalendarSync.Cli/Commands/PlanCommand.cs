using System.CommandLine;
using Spectre.Console;
using CalendarSync.Cli.Config;
using CalendarSync.Cli.Providers;

namespace CalendarSync.Cli.Commands;

public static class PlanCommand
{
    // Embedded in the event body so we can identify placeholders on re-runs
    private const string SyncMarkerPrefix = "[CalSync:";

    public static Command Build(ProviderRegistry registry)
    {
        var cmd = new Command("plan", "Plan upcoming events — select source events to block off in your other calendars");

        var startOpt = new Option<DateOnly?>("--start") { Description = "Start date (yyyy-MM-dd). Defaults to today." };
        var endOpt = new Option<DateOnly?>("--end") { Description = "End date, exclusive (yyyy-MM-dd). Defaults to 7 days from start." };
        cmd.Add(startOpt);
        cmd.Add(endOpt);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var config = ConfigManager.Load();

            var accountsWithCalendars = config.Accounts
                .Where(a => a.SelectedCalendars.Count > 0)
                .ToList();

            if (accountsWithCalendars.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No calendars configured. Run 'setup' first.[/]");
                return;
            }

            var writableTargets = accountsWithCalendars
                .Where(a => !registry.Get(a.Provider).IsReadOnly)
                .ToList();

            if (writableTargets.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No writable calendars configured. Add a Microsoft or Google account.[/]");
                return;
            }

            var startDate = parseResult.GetValue(startOpt) ?? DateOnly.FromDateTime(DateTime.Today);
            var endDate = parseResult.GetValue(endOpt) ?? startDate.AddDays(7);

            var from = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue));
            var to = new DateTimeOffset(endDate.ToDateTime(TimeOnly.MinValue));

            AnsiConsole.MarkupLine($"\nPlanning [bold]{from:dddd, MMM d}[/] – [bold]{to.AddDays(-1):dddd, MMM d}[/]\n");

            // Fetch events from all configured calendars
            var allEvents = await FetchAllEventsAsync(config, registry, accountsWithCalendars, from, to, ct);

            if (allEvents.Count == 0)
            {
                AnsiConsole.MarkupLine("[grey]No events found across any calendar for the selected period.[/]");
                return;
            }

            // Flatten to a list of (account, calendar) target pairs — each calendar is independently a target
            var targets = writableTargets
                .SelectMany(a => a.SelectedCalendars.Select(c => (Account: a, Calendar: c)))
                .ToList();

            foreach (var (targetAccount, targetCalendar) in targets)
            {
                await ProcessTargetAsync(config, registry, targetAccount, targetCalendar, allEvents, ct);
            }

            ConfigManager.Save(config);
        });

        return cmd;
    }

    // ── Fetching ─────────────────────────────────────────────────────────────

    private static async Task<List<SourceEvent>> FetchAllEventsAsync(
        AppConfig config,
        ProviderRegistry registry,
        List<AccountConfig> accounts,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        var results = new List<SourceEvent>();
        var warnings = new List<string>();

        await AnsiConsole.Status().StartAsync("Fetching events...", async _ =>
        {
            foreach (var account in accounts)
            {
                var provider = registry.Get(account.Provider);
                foreach (var cal in account.SelectedCalendars)
                {
                    try
                    {
                        var events = await provider.GetEventsAsync(account, cal.Id, from, to, ct);
                        results.AddRange(events.Select(e => new SourceEvent(e, account, cal)));
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"[yellow]Warning:[/] {account.DisplayName ?? account.Id} / {cal.Name}: {Markup.Escape(ex.Message)}");
                    }
                }
            }
        });

        foreach (var w in warnings)
            AnsiConsole.MarkupLine(w);

        return results;
    }

    // ── Processing one target calendar ────────────────────────────────────────

    private static async Task ProcessTargetAsync(
        AppConfig config,
        ProviderRegistry registry,
        AccountConfig targetAccount,
        SelectedCalendar targetCalendar,
        List<SourceEvent> allEvents,
        CancellationToken ct)
    {
        // Sources = every event across every calendar except this specific target calendar,
        // excluding placeholder events that were themselves created by CalSync
        var sourceEvents = allEvents
            .Where(e => !(e.Account.Id == targetAccount.Id && e.Calendar.Id == targetCalendar.Id))
            .Where(e => !IsSyncPlaceholder(e))
            .OrderBy(e => e.Event.Start)
            .ToList();

        if (sourceEvents.Count == 0)
        {
            AnsiConsole.MarkupLine($"\n[grey]No source events to sync to {targetCalendar.Name}.[/]");
            return;
        }

        // Find sync entries relevant to this target calendar
        var sourceEventIds = sourceEvents.Select(e => e.Event.Id).ToHashSet();
        var existingEntries = config.SyncedEvents
            .Where(e => e.TargetAccountId == targetAccount.Id
                     && e.TargetCalendarId == targetCalendar.Id
                     && sourceEventIds.Contains(e.SourceEventId))
            .ToDictionary(e => e.SourceEventId);

        // Self-heal: also scan placeholder events already in the target calendar.
        // If we find CalSync markers that aren't in config (e.g. config was lost or
        // the event was created on another machine), add them so re-runs don't duplicate.
        foreach (var placeholder in allEvents
            .Where(e => e.Account.Id == targetAccount.Id && e.Calendar.Id == targetCalendar.Id)
            .Where(e => IsSyncPlaceholder(e)))
        {
            var parsed = ParseSyncMarker(placeholder.Event.BodyPreview);
            if (parsed is not { } src) continue;
            if (!sourceEventIds.Contains(src.SourceEventId)) continue;
            if (existingEntries.ContainsKey(src.SourceEventId)) continue;

            var healed = new SyncEntry
            {
                SourceAccountId  = src.SourceAccountId,
                SourceCalendarId = src.SourceCalendarId,
                SourceEventId    = src.SourceEventId,
                TargetAccountId  = targetAccount.Id,
                TargetCalendarId = targetCalendar.Id,
                TargetEventId    = placeholder.Event.Id
            };
            existingEntries[src.SourceEventId] = healed;
            config.SyncedEvents.Add(healed);
        }

        // Show the multi-select
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"Target: [bold]{targetCalendar.Name}[/] [grey]({targetAccount.DisplayName ?? targetAccount.Id})[/]").LeftJustified());

        var selected = PromptEventSelection(sourceEvents, existingEntries);

        // Compute diff
        var selectedIds = selected.Select(e => e.Event.Id).ToHashSet();
        var toCreate = selected.Where(e => !existingEntries.ContainsKey(e.Event.Id)).ToList();
        var toUpdate = selected.Where(e => existingEntries.ContainsKey(e.Event.Id)).ToList();
        var toDelete = existingEntries.Values
            .Where(e => !selectedIds.Contains(e.SourceEventId))
            .ToList();

        if (toCreate.Count == 0 && toUpdate.Count == 0 && toDelete.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No changes.[/]");
            return;
        }

        var provider = registry.Get(targetAccount.Provider);

        await AnsiConsole.Status().StartAsync("Applying changes...", async _ =>
        {
            foreach (var src in toCreate)
                await CreatePlaceholderAsync(config, provider, targetAccount, targetCalendar, src, ct);

            foreach (var src in toUpdate)
                await UpdatePlaceholderAsync(provider, targetAccount, existingEntries[src.Event.Id], src, ct);

            foreach (var entry in toDelete)
                await DeletePlaceholderAsync(config, provider, targetAccount, entry, ct);
        });

        AnsiConsole.MarkupLine(
            $"[green]Done:[/] {toCreate.Count} created, {toUpdate.Count} updated, {toDelete.Count} removed.");
    }

    // ── Prompt ────────────────────────────────────────────────────────────────

    private static List<SourceEvent> PromptEventSelection(
        List<SourceEvent> sourceEvents,
        Dictionary<string, SyncEntry> existingEntries)
    {
        // Use event IDs as prompt items; look up SourceEvent via dictionary
        var eventMap = sourceEvents.ToDictionary(e => e.Event.Id);

        var prompt = new MultiSelectionPrompt<string>()
            .Title("Select events to block off (space to toggle, enter to confirm):")
            .UseConverter(id => eventMap.TryGetValue(id, out var src)
                ? FormatEventLabel(src, existingEntries.ContainsKey(id))
                : id)
            .PageSize(20)
            .NotRequired();

        foreach (var dayGroup in sourceEvents.GroupBy(e => e.Event.Start.Date).OrderBy(g => g.Key))
        {
            var dayLabel = dayGroup.Key.ToString("dddd, MMM d");
            var groupItem = prompt.AddChoice(dayLabel);
            foreach (var src in dayGroup.OrderBy(e => e.Event.Start))
                groupItem.AddChild(src.Event.Id);
        }

        // Pre-select previously synced events
        foreach (var entry in existingEntries.Values)
        {
            if (eventMap.ContainsKey(entry.SourceEventId))
                prompt.Select(entry.SourceEventId);
        }

        try
        {
            var selectedIds = AnsiConsole.Prompt(prompt);
            return selectedIds.Select(id => eventMap[id]).ToList();
        }
        catch
        {
            // User may press Ctrl+C or prompt may be empty
            return [];
        }
    }

    private static bool IsSyncPlaceholder(SourceEvent src) =>
        src.Event.BodyPreview?.Contains(SyncMarkerPrefix, StringComparison.Ordinal) == true;

    private static string FormatEventLabel(SourceEvent src, bool isSynced)
    {
        var time = src.Event.IsAllDay
            ? "all-day "
            : $"{src.Event.Start:HH:mm}–{src.Event.End:HH:mm}";

        var account = Markup.Escape(src.Account.DisplayName ?? src.Account.Id);
        var calendar = Markup.Escape(src.Calendar.Name);
        var subject = Markup.Escape(src.Event.Subject);
        var prefix = $"[steelblue1][[{account}:{calendar}]][/]";
        var synced = isSynced ? " [grey](synced)[/]" : "";

        return $"{time}  {prefix}  [white]{subject}[/]{synced}";
    }

    // ── Create / Update / Delete ──────────────────────────────────────────────

    private static async Task CreatePlaceholderAsync(
        AppConfig config,
        ICalendarProvider provider,
        AccountConfig targetAccount,
        SelectedCalendar targetCalendar,
        SourceEvent src,
        CancellationToken ct)
    {
        var body = BuildSyncMarker(src.Account.Id, src.Calendar.Id, src.Event.Id);
        var newEvt = new NewEvent(
            Subject: $"[Event from {src.Account.DisplayName ?? src.Account.Id}:{src.Calendar.Name}]",
            Start: src.Event.Start,
            End: src.Event.End,
            IsAllDay: src.Event.IsAllDay,
            Body: body);

        var targetEventId = await provider.CreateEventAsync(targetAccount, targetCalendar.Id, newEvt, ct);

        config.SyncedEvents.Add(new SyncEntry
        {
            SourceAccountId = src.Account.Id,
            SourceCalendarId = src.Calendar.Id,
            SourceEventId = src.Event.Id,
            TargetAccountId = targetAccount.Id,
            TargetCalendarId = targetCalendar.Id,
            TargetEventId = targetEventId
        });
    }

    private static async Task UpdatePlaceholderAsync(
        ICalendarProvider provider,
        AccountConfig targetAccount,
        SyncEntry entry,
        SourceEvent src,
        CancellationToken ct)
    {
        var body = BuildSyncMarker(src.Account.Id, src.Calendar.Id, src.Event.Id);
        var updatedEvt = new NewEvent(
            Subject: $"[Event from {src.Account.DisplayName ?? src.Account.Id}:{src.Calendar.Name}]",
            Start: src.Event.Start,
            End: src.Event.End,
            IsAllDay: src.Event.IsAllDay,
            Body: body);

        await provider.UpdateEventAsync(targetAccount, entry.TargetCalendarId, entry.TargetEventId, updatedEvt, ct);
    }

    private static async Task DeletePlaceholderAsync(
        AppConfig config,
        ICalendarProvider provider,
        AccountConfig targetAccount,
        SyncEntry entry,
        CancellationToken ct)
    {
        await provider.DeleteEventAsync(targetAccount, entry.TargetCalendarId, entry.TargetEventId, ct);
        config.SyncedEvents.Remove(entry);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string BuildSyncMarker(string accountId, string calendarId, string eventId) =>
        $"{SyncMarkerPrefix} src={accountId}|{calendarId}|{eventId}]";

    private record ParsedMarker(string SourceAccountId, string SourceCalendarId, string SourceEventId);

    private static ParsedMarker? ParseSyncMarker(string? body)
    {
        if (body is null) return null;
        var srcIndex = body.IndexOf("src=", StringComparison.Ordinal);
        if (srcIndex < 0) return null;
        var value = body[(srcIndex + 4)..].TrimEnd(']').Trim();
        var parts = value.Split('|');
        return parts.Length == 3
            ? new ParsedMarker(parts[0], parts[1], parts[2])
            : null;
    }
}

/// <summary>A source event with its account and calendar context, used for display and diff logic.</summary>
internal record SourceEvent(Providers.CalendarEvent Event, AccountConfig Account, SelectedCalendar Calendar);
