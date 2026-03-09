# CalendarSync

<p align="center">
  <img src="assets/logo.svg" width="128" height="128" alt="CalendarSync logo"/>
</p>

A .NET CLI tool for consultants and anyone juggling multiple calendar identities. Run it each week to get an overview of your events across all calendars, then select which ones to block off as placeholders in your other calendars.

## Features

- **Multi-provider** — Microsoft 365 / Entra, personal Microsoft (Outlook.com / Hotmail / Live.com), Google Workspace, personal Google (Gmail), and read-only ICS feeds
- **Interactive planning** — pick exactly which events to block off, per target calendar
- **Smart re-runs** — on subsequent runs, previously synced events are pre-selected and updated (or removed) as needed
- **Native calendar integration** — placeholders get an Outlook category (`CalSync`) and a Google Calendar colour (Blueberry) so they're easy to spot
- **WSL-friendly** — Microsoft device code flow and a custom Google OAuth receiver that prints the URL instead of launching a Linux browser
- **Offline-tolerant** — sync state is self-healing; if config is lost, markers embedded in placeholder events are re-parsed automatically

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Installation

```bash
git clone https://github.com/tsolbjor/calendarsync.git
cd tsol.calendarsync
dotnet build
```

Run via:

```bash
dotnet run --project src/CalendarSync.Cli -- <command>
```

Or publish a self-contained binary:

**Linux / macOS**
```bash
dotnet publish src/CalendarSync.Cli -c Release -r linux-x64 --self-contained -o ./publish
./publish/CalendarSync <command>
```

**Windows (PowerShell)**
```powershell
dotnet publish src/CalendarSync.Cli -c Release -r win-x64 --self-contained -o ./publish
./publish/CalendarSync.exe <command>
```

## Quick start

```bash
# 1. Add your accounts
calendarsync account add microsoft  --id personal-ms                                          # personal Microsoft account
calendarsync account add microsoft  --id work-ms   --tenant-id <tenant> --client-id <client>  # work / Entra account
calendarsync account add google-personal --id personal
calendarsync account add readonly   --id holidays  --url https://calendar.google.com/calendar/ical/.../basic.ics

# 2. Authenticate and pick which calendars to include
calendarsync setup

# 3. Plan the next 7 days
calendarsync plan

# 4. Or specify a custom range
calendarsync plan --start 2026-03-10 --end 2026-03-17
```

## Commands

| Command | Description |
|---|---|
| `account add microsoft` | Add a Microsoft account (personal Outlook.com / Hotmail, or work Entra / Microsoft 365) |
| `account add google` | Add a Google Workspace account (bring your own OAuth credentials) |
| `account add google-personal` | Add a personal Gmail account (credentials bundled) |
| `account add readonly` | Add a read-only ICS feed (source only) |
| `account list` | Show all configured accounts and their auth status |
| `account login <id>` | Sign in to an account |
| `account logout <id>` | Sign out from an account |
| `account remove <id>` | Remove an account from config |
| `setup` | Authenticate all accounts and select which calendars to include |
| `plan` | Interactively plan the next 7 days (create/update/delete placeholders) |
| `validate` | Check auth and list raw events per calendar — useful for debugging |

## Setting up OAuth credentials

### Microsoft

#### Personal Microsoft accounts (Outlook.com, Hotmail, Live.com)

No app registration needed — the app ships with a built-in OAuth client that supports personal Microsoft accounts.

```bash
calendarsync account add microsoft --id personal-ms
```

#### Work / Microsoft 365 accounts (Entra)

1. Register an app in [Azure Portal](https://portal.azure.com) → App registrations
2. Add a **Mobile and desktop application** redirect URI: `http://localhost`
3. Enable **Allow public client flows** under Authentication
4. Note the **Application (client) ID** and **Directory (tenant) ID**

```bash
calendarsync account add microsoft \
  --id work-ms \
  --tenant-id <tenant-id> \
  --client-id <client-id> \
  --device-code   # recommended for WSL
```

### Google Workspace

1. Create a project in [Google Cloud Console](https://console.cloud.google.com)
2. Enable the **Google Calendar API**
3. Create an OAuth client ID — type **Desktop app**
4. Add your account as a test user under OAuth consent screen

```bash
calendarsync account add google \
  --id work-google \
  --client-id <client-id> \
  --client-secret <client-secret>
```

### Personal Gmail

No credentials needed — the app ships with a bundled OAuth client for personal accounts.

```bash
calendarsync account add google-personal --id personal
```

### Read-only ICS feed

```bash
calendarsync account add readonly \
  --id holidays \
  --url "https://calendar.google.com/calendar/ical/.../basic.ics"
```

Google Calendar's private ICS URL can be found under **Calendar settings → Integrate calendar → Secret address in iCal format**.

## Configuration

Config is stored at `~/.calendarsync/config.json` (mode `600` on Unix). Token caches live at `~/.calendarsync/tokens/`. No credentials are ever stored in the project directory.

## WSL notes

- **Microsoft**: use `--device-code` when adding the account. The device code prompt gives you a URL to open in your Windows browser.
- **Google**: the OAuth flow prints a URL to the terminal and listens on `localhost` for the callback. Open the URL in your Windows browser — WSL2 forwards localhost ports automatically.

## Screenshots

See [docs/screenshots.md](docs/screenshots.md) for a step-by-step walkthrough with sample
terminal output covering account setup, login, calendar selection, validation, and planning.

## License

MIT — see [LICENSE](LICENSE).

## Legal

- [Privacy Policy](PRIVACY.md)
- [Terms of Service](TERMS.md)
