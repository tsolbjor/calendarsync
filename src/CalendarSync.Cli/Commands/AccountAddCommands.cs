using System.CommandLine;
using Spectre.Console;
using CalendarSync.Cli.Config;

namespace CalendarSync.Cli.Commands;

public static class AccountAddCommands
{
    public static Command Build()
    {
        var addCmd = new Command("add", "Add a calendar account");

        addCmd.Add(BuildMicrosoftCommand());
        addCmd.Add(BuildGoogleCommand());
        addCmd.Add(BuildGooglePersonalCommand());
        addCmd.Add(BuildReadOnlyCommand());

        return addCmd;
    }

    private static Command BuildMicrosoftCommand()
    {
        var idOpt = new Option<string>("--id") { Required = true, Description = "Friendly name (e.g. 'work-ms')" };
        var tenantIdOpt = new Option<string>("--tenant-id") { Required = true, Description = "Entra tenant ID or domain" };
        var clientIdOpt = new Option<string>("--client-id") { Required = true, Description = "App registration client ID" };
        var displayNameOpt = new Option<string?>("--display-name") { Description = "Optional display name" };
        var deviceCodeOpt = new Option<bool>("--device-code")
        {
            Description = "Use device code flow instead of browser (required for WSL/headless). " +
                          "Needs 'Allow public client flows' enabled in the app registration."
        };

        var cmd = new Command("microsoft", "Add a Microsoft Entra / Microsoft 365 account");
        cmd.Add(idOpt);
        cmd.Add(tenantIdOpt);
        cmd.Add(clientIdOpt);
        cmd.Add(displayNameOpt);
        cmd.Add(deviceCodeOpt);

        cmd.SetAction((parseResult) =>
        {
            var config = ConfigManager.Load();
            var id = parseResult.GetValue(idOpt)!;

            if (config.Accounts.Any(a => a.Id == id))
            {
                AnsiConsole.MarkupLine($"[red]Account '{id}' already exists.[/]");
                return;
            }

            var useDeviceCode = parseResult.GetValue(deviceCodeOpt);
            config.Accounts.Add(new AccountConfig
            {
                Id = id,
                Provider = CalendarProvider.Microsoft,
                DisplayName = parseResult.GetValue(displayNameOpt),
                TenantId = parseResult.GetValue(tenantIdOpt),
                ClientId = parseResult.GetValue(clientIdOpt),
                UseDeviceCode = useDeviceCode
            });

            ConfigManager.Save(config);
            var hint = useDeviceCode ? " (device code flow)" : "";
            AnsiConsole.MarkupLine($"[green]Microsoft account '[bold]{id}[/]' added{hint}. Run 'account login {id}' to sign in.[/]");
        });

        return cmd;
    }

    private static Command BuildGoogleCommand()
    {
        var idOpt = new Option<string>("--id") { Required = true, Description = "Friendly name (e.g. 'work-google')" };
        var clientIdOpt = new Option<string>("--client-id") { Required = true, Description = "OAuth2 client ID from Google Cloud Console" };
        var clientSecretOpt = new Option<string>("--client-secret") { Required = true, Description = "OAuth2 client secret from Google Cloud Console" };
        var displayNameOpt = new Option<string?>("--display-name") { Description = "Optional display name" };

        var cmd = new Command("google", "Add a Google Calendar account");
        cmd.Add(idOpt);
        cmd.Add(clientIdOpt);
        cmd.Add(clientSecretOpt);
        cmd.Add(displayNameOpt);

        cmd.SetAction((parseResult) =>
        {
            var config = ConfigManager.Load();
            var id = parseResult.GetValue(idOpt)!;

            if (config.Accounts.Any(a => a.Id == id))
            {
                AnsiConsole.MarkupLine($"[red]Account '{id}' already exists.[/]");
                return;
            }

            config.Accounts.Add(new AccountConfig
            {
                Id = id,
                Provider = CalendarProvider.Google,
                DisplayName = parseResult.GetValue(displayNameOpt),
                GoogleClientId = parseResult.GetValue(clientIdOpt),
                GoogleClientSecret = parseResult.GetValue(clientSecretOpt)
            });

            ConfigManager.Save(config);
            AnsiConsole.MarkupLine($"[green]Google account '[bold]{id}[/]' added. Run 'account login {id}' to authorise.[/]");
        });

        return cmd;
    }

    private static Command BuildGooglePersonalCommand()
    {
        var idOpt = new Option<string>("--id") { Required = true, Description = "Friendly name (e.g. 'personal')" };
        var displayNameOpt = new Option<string?>("--display-name") { Description = "Optional display name" };

        var cmd = new Command("google-personal", "Add a personal Google (Gmail) account");
        cmd.Add(idOpt);
        cmd.Add(displayNameOpt);

        cmd.SetAction((parseResult) =>
        {
            var config = ConfigManager.Load();
            var id = parseResult.GetValue(idOpt)!;

            if (config.Accounts.Any(a => a.Id == id))
            {
                AnsiConsole.MarkupLine($"[red]Account '{id}' already exists.[/]");
                return;
            }

            config.Accounts.Add(new AccountConfig
            {
                Id = id,
                Provider = CalendarProvider.GooglePersonal,
                DisplayName = parseResult.GetValue(displayNameOpt)
            });

            ConfigManager.Save(config);
            AnsiConsole.MarkupLine($"[green]Personal Google account '[bold]{id}[/]' added. Run 'setup' or 'account login {id}' to authorise.[/]");
        });

        return cmd;
    }

    private static Command BuildReadOnlyCommand()
    {
        var idOpt = new Option<string>("--id") { Required = true, Description = "Friendly name (e.g. 'holidays')" };
        var urlOpt = new Option<string>("--url") { Required = true, Description = "Public ICS feed URL" };
        var displayNameOpt = new Option<string?>("--display-name") { Description = "Optional display name" };

        var cmd = new Command("readonly", "Add a read-only ICS feed (source only, no event creation)");
        cmd.Add(idOpt);
        cmd.Add(urlOpt);
        cmd.Add(displayNameOpt);

        cmd.SetAction((parseResult) =>
        {
            var config = ConfigManager.Load();
            var id = parseResult.GetValue(idOpt)!;

            if (config.Accounts.Any(a => a.Id == id))
            {
                AnsiConsole.MarkupLine($"[red]Account '{id}' already exists.[/]");
                return;
            }

            config.Accounts.Add(new AccountConfig
            {
                Id = id,
                Provider = CalendarProvider.ReadOnly,
                DisplayName = parseResult.GetValue(displayNameOpt),
                IcsUrl = parseResult.GetValue(urlOpt)
            });

            ConfigManager.Save(config);
            AnsiConsole.MarkupLine($"[green]Read-only feed '[bold]{id}[/]' added.[/]");
        });

        return cmd;
    }
}
