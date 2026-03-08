using System.CommandLine;
using Spectre.Console;
using CalendarSync.Cli.Config;
using CalendarSync.Cli.Providers;

namespace CalendarSync.Cli.Commands;

public static class ValidateCommand
{
    public static Command Build(ProviderRegistry registry)
    {
        var cmd = new Command("validate", "Check authentication and list fetched events for every configured calendar");

        var startOpt = new Option<DateOnly?>("--start") { Description = "Start date (yyyy-MM-dd). Defaults to today." };
        var endOpt   = new Option<DateOnly?>("--end")   { Description = "End date, exclusive (yyyy-MM-dd). Defaults to 7 days from start." };
        cmd.Add(startOpt);
        cmd.Add(endOpt);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var config = ConfigManager.Load();

            if (config.Accounts.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No accounts configured. Run 'account add' first.[/]");
                return;
            }

            var startDate = parseResult.GetValue(startOpt) ?? DateOnly.FromDateTime(DateTime.Today);
            var endDate   = parseResult.GetValue(endOpt)   ?? startDate.AddDays(7);
            var from = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue));
            var to   = new DateTimeOffset(endDate.ToDateTime(TimeOnly.MinValue));

            AnsiConsole.MarkupLine($"\nValidating [bold]{from:dddd, MMM d}[/] – [bold]{to.AddDays(-1):dddd, MMM d}[/]\n");

            foreach (var account in config.Accounts)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule($"[bold]{account.DisplayName ?? account.Id}[/] [grey]({account.Provider})[/]").LeftJustified());

                var provider = registry.Get(account.Provider);

                // ── Auth check ────────────────────────────────────────────────
                bool authed;
                try
                {
                    authed = await AnsiConsole.Status().StartAsync(
                        "Checking auth...", _ => provider.IsAuthenticatedAsync(account, ct));
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Auth check failed: {Markup.Escape(ex.Message)}[/]");
                    continue;
                }

                if (!authed)
                {
                    AnsiConsole.MarkupLine("[red]Not authenticated.[/] Run 'account login' or 'setup'.");
                    continue;
                }

                AnsiConsole.MarkupLine(provider.IsReadOnly ? "[blue]Authenticated (read-only)[/]" : "[green]Authenticated[/]");

                if (account.SelectedCalendars.Count == 0)
                {
                    AnsiConsole.MarkupLine("[grey]No calendars selected. Run 'setup'.[/]");
                    continue;
                }

                // ── Fetch events per calendar ─────────────────────────────────
                foreach (var cal in account.SelectedCalendars)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"  Calendar: [bold]{Markup.Escape(cal.Name)}[/] [grey](id: {Markup.Escape(cal.Id)})[/]");

                    IReadOnlyList<Providers.CalendarEvent> events;
                    try
                    {
                        events = await AnsiConsole.Status().StartAsync(
                            $"  Fetching events...",
                            _ => provider.GetEventsAsync(account, cal.Id, from, to, ct));
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"  [red]Fetch failed: {Markup.Escape(ex.Message)}[/]");
                        continue;
                    }

                    if (events.Count == 0)
                    {
                        AnsiConsole.MarkupLine("  [grey]No events found in this range.[/]");
                        continue;
                    }

                    AnsiConsole.MarkupLine($"  [green]{events.Count} event(s) found:[/]");

                    var table = new Table().NoBorder().HideHeaders();
                    table.AddColumn("Time");
                    table.AddColumn("Subject");
                    table.AddColumn("Body");

                    foreach (var e in events.OrderBy(e => e.Start))
                    {
                        var time = e.IsAllDay
                            ? "all-day"
                            : $"{e.Start:HH:mm}–{e.End:HH:mm}";

                        var bodyPreview = e.BodyPreview is { Length: > 0 } b
                            ? $"[grey]{Markup.Escape(b[..Math.Min(60, b.Length)])}[/]"
                            : "[grey](no body)[/]";

                        table.AddRow(
                            $"[grey]{e.Start:ddd d} {time}[/]",
                            Markup.Escape(e.Subject),
                            bodyPreview);
                    }

                    AnsiConsole.Write(table);
                }
            }

            AnsiConsole.WriteLine();
        });

        return cmd;
    }
}
