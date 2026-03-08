using System.CommandLine;
using Spectre.Console;
using CalendarSync.Cli.Config;
using CalendarSync.Cli.Providers;

namespace CalendarSync.Cli.Commands;

public static class AccountCommands
{
    public static Command Build(ProviderRegistry registry)
    {
        var accountCmd = new Command("account", "Manage calendar accounts");

        accountCmd.Add(AccountAddCommands.Build());
        accountCmd.Add(BuildListCommand(registry));
        accountCmd.Add(BuildRemoveCommand());
        accountCmd.Add(BuildLoginCommand(registry));
        accountCmd.Add(BuildLogoutCommand(registry));

        return accountCmd;
    }

    private static Command BuildListCommand(ProviderRegistry registry)
    {
        var cmd = new Command("list", "List all configured accounts");

        cmd.SetAction(async (parseResult, ct) =>
        {
            var config = ConfigManager.Load();

            if (config.Accounts.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No accounts configured. Use 'account add <provider>' to add one.[/]");
                return;
            }

            var table = new Table();
            table.AddColumn("ID");
            table.AddColumn("Provider");
            table.AddColumn("Display Name");
            table.AddColumn("Identity");
            table.AddColumn("Status");

            foreach (var account in config.Accounts)
            {
                var provider = registry.Get(account.Provider);

                string status;
                if (provider.IsReadOnly)
                {
                    status = "[blue]Read-only[/]";
                }
                else
                {
                    bool authed;
                    try { authed = await provider.IsAuthenticatedAsync(account, ct); }
                    catch { authed = false; }
                    status = authed ? "[green]Authenticated[/]" : "[grey]Not signed in[/]";
                }

                var identity = account.Provider switch
                {
                    CalendarProvider.Microsoft => account.TenantId ?? "-",
                    CalendarProvider.Google or CalendarProvider.GooglePersonal =>
                        account.GoogleClientId is { } cid
                            ? cid[..Math.Min(12, cid.Length)] + "..."
                            : "-",
                    CalendarProvider.ReadOnly => account.IcsUrl ?? "-",
                    _ => "-"
                };

                var providerLabel = provider.IsReadOnly
                    ? $"{account.Provider} [blue](read-only)[/]"
                    : account.Provider.ToString();

                table.AddRow(
                    account.Id,
                    providerLabel,
                    account.DisplayName ?? "-",
                    identity,
                    status);
            }

            AnsiConsole.Write(table);
        });

        return cmd;
    }

    private static Command BuildRemoveCommand()
    {
        var idArg = new Argument<string>("id") { Description = "Account ID to remove" };
        var cmd = new Command("remove", "Remove an account");
        cmd.Add(idArg);

        cmd.SetAction((parseResult) =>
        {
            var id = parseResult.GetValue(idArg)!;
            var config = ConfigManager.Load();
            var account = config.Accounts.FirstOrDefault(a => a.Id == id);

            if (account is null)
            {
                AnsiConsole.MarkupLine($"[red]Account '{id}' not found.[/]");
                return;
            }

            config.Accounts.Remove(account);
            ConfigManager.Save(config);
            AnsiConsole.MarkupLine($"[green]Account '[bold]{id}[/]' removed.[/]");
        });

        return cmd;
    }

    private static Command BuildLoginCommand(ProviderRegistry registry)
    {
        var idArg = new Argument<string>("id") { Description = "Account ID to sign in to" };
        var cmd = new Command("login", "Sign in to a calendar account (validates ICS URL for read-only feeds)");
        cmd.Add(idArg);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var id = parseResult.GetValue(idArg)!;
            var config = ConfigManager.Load();
            var account = config.Accounts.FirstOrDefault(a => a.Id == id);

            if (account is null)
            {
                AnsiConsole.MarkupLine($"[red]Account '{id}' not found. Use 'account add' first.[/]");
                return;
            }

            var provider = registry.Get(account.Provider);
            var label = $"[bold]{account.DisplayName ?? account.Id}[/] ({account.Provider})";

            if (provider.IsReadOnly)
                AnsiConsole.MarkupLine($"Validating read-only feed {label}...");
            else
                AnsiConsole.MarkupLine($"Signing in to {label}...");

            try
            {
                await provider.LoginAsync(account, ct);
                AnsiConsole.MarkupLine(provider.IsReadOnly
                    ? "[green]Feed is reachable and valid.[/]"
                    : "[green]Successfully signed in.[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Failed: {Markup.Escape(ex.Message)}[/]");
            }
        });

        return cmd;
    }

    private static Command BuildLogoutCommand(ProviderRegistry registry)
    {
        var idArg = new Argument<string>("id") { Description = "Account ID to sign out from" };
        var cmd = new Command("logout", "Sign out from a calendar account");
        cmd.Add(idArg);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var id = parseResult.GetValue(idArg)!;
            var config = ConfigManager.Load();
            var account = config.Accounts.FirstOrDefault(a => a.Id == id);

            if (account is null)
            {
                AnsiConsole.MarkupLine($"[red]Account '{id}' not found.[/]");
                return;
            }

            var provider = registry.Get(account.Provider);

            if (provider.IsReadOnly)
            {
                AnsiConsole.MarkupLine($"[yellow]'{id}' is a read-only feed — nothing to sign out from.[/]");
                return;
            }

            try
            {
                await provider.LogoutAsync(account, ct);
                AnsiConsole.MarkupLine($"[green]Signed out from '[bold]{id}[/]'.[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Logout failed: {Markup.Escape(ex.Message)}[/]");
            }
        });

        return cmd;
    }
}
