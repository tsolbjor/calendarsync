using System.CommandLine;
using Spectre.Console;
using CalendarSync.Cli.Config;
using CalendarSync.Cli.Providers;

namespace CalendarSync.Cli.Commands;

public static class SetupCommand
{
    public static Command Build(ProviderRegistry registry)
    {
        var cmd = new Command("setup", "Authenticate accounts and select calendars for planning");

        cmd.SetAction(async (parseResult, ct) =>
        {
            var config = ConfigManager.Load();

            if (config.Accounts.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No accounts configured. Use 'account add' first.[/]");
                return;
            }

            var anyChanges = false;

            foreach (var account in config.Accounts)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.Write(
                    new Rule($"[bold]{account.DisplayName ?? account.Id}[/] [grey]({account.Provider})[/]")
                        .LeftJustified());

                var provider = registry.Get(account.Provider);

                // ── Step 1: ensure authenticated ─────────────────────────────
                var isReady = await EnsureAuthenticatedAsync(account, provider, ct);
                if (!isReady) continue;

                // ── Step 2: fetch calendars ───────────────────────────────────
                IReadOnlyList<CalendarInfo> calendars;
                try
                {
                    calendars = await AnsiConsole
                        .Status()
                        .StartAsync("Fetching calendars...",
                            _ => provider.GetCalendarsAsync(account, ct));
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Could not fetch calendars: {Markup.Escape(ex.Message)}[/]");
                    continue;
                }

                if (calendars.Count == 0)
                {
                    AnsiConsole.MarkupLine("[yellow]No calendars found for this account.[/]");
                    continue;
                }

                // ── Step 3: select calendars ──────────────────────────────────
                var selected = SelectCalendars(account, provider, calendars);
                account.SelectedCalendars = selected
                    .Select(c => new SelectedCalendar { Id = c.Id, Name = c.Name })
                    .ToList();

                anyChanges = true;
            }

            if (anyChanges)
            {
                ConfigManager.Save(config);
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[green]Setup complete. Run 'plan' to start planning your week.[/]");
            }
            else
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[grey]No changes saved.[/]");
            }
        });

        return cmd;
    }

    private static async Task<bool> EnsureAuthenticatedAsync(
        AccountConfig account, ICalendarProvider provider, CancellationToken ct)
    {
        if (provider.IsReadOnly)
        {
            var reachable = await AnsiConsole
                .Status()
                .StartAsync("Checking feed...", _ => provider.IsAuthenticatedAsync(account, ct));

            if (!reachable)
            {
                AnsiConsole.MarkupLine("[red]Feed URL is not reachable. Skipping.[/]");
                return false;
            }

            AnsiConsole.MarkupLine("[blue]Read-only feed — reachable[/]");
            return true;
        }

        bool authed;
        try
        {
            authed = await AnsiConsole
                .Status()
                .StartAsync("Checking authentication...", _ => provider.IsAuthenticatedAsync(account, ct));
        }
        catch
        {
            authed = false;
        }

        if (authed)
        {
            AnsiConsole.MarkupLine("[green]Authenticated[/]");
            return true;
        }

        AnsiConsole.MarkupLine("[yellow]Not signed in.[/]");

        if (!AnsiConsole.Confirm("Sign in now?", defaultValue: true))
            return false;

        try
        {
            await provider.LoginAsync(account, ct);
            AnsiConsole.MarkupLine("[green]Signed in.[/]");
            return true;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Login failed: {Markup.Escape(ex.Message)}[/]");
            return false;
        }
    }

    private static IReadOnlyList<CalendarInfo> SelectCalendars(
        AccountConfig account, ICalendarProvider provider, IReadOnlyList<CalendarInfo> calendars)
    {
        // Read-only feeds represent a single calendar — auto-select without prompting
        if (provider.IsReadOnly)
        {
            var names = string.Join(", ", calendars.Select(c => $"[bold]{Markup.Escape(c.Name)}[/]"));
            AnsiConsole.MarkupLine($"Auto-selected: {names}");
            return calendars;
        }

        var previousIds = account.SelectedCalendars.Select(c => c.Id).ToHashSet();

        var prompt = new MultiSelectionPrompt<CalendarInfo>()
            .Title("Select calendars to include in planning:")
            .NotRequired()
            .UseConverter(c => c.Name)
            .AddChoices(calendars);

        // Re-check any previously selected calendars
        foreach (var cal in calendars.Where(c => previousIds.Contains(c.Id)))
            prompt.Select(cal);

        return AnsiConsole.Prompt(prompt);
    }
}
