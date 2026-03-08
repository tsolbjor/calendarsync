# Privacy Policy

**Effective date:** 2026-03-08

CalendarSync is an open-source command-line tool developed and maintained by Thomas Olbjor. This policy describes what data the application accesses, how it is used, and where it is stored.

## What data CalendarSync accesses

When you connect a calendar account, CalendarSync requests read and write access to your calendar data via the provider's official API (Microsoft Graph or Google Calendar API). Specifically, it may access:

- Calendar names and IDs
- Event titles, start/end times, location, and body/description
- Your user identifier (e.g. email address or user ID) to associate stored tokens with your account

## How data is used

CalendarSync uses calendar data exclusively to:

1. Display your upcoming events in the terminal so you can choose which ones to sync
2. Create, update, or delete placeholder "busy block" events in your other calendars based on your selections

**No data is transmitted to any server operated by this project.** All API calls go directly from your machine to Microsoft or Google.

## Where data is stored

All data stays on your local machine:

| What | Where |
|---|---|
| Configuration (accounts, selected calendars, sync state) | `~/.calendarsync/config.json` |
| OAuth access and refresh tokens | `~/.calendarsync/tokens/` |

Both paths are created with owner-only permissions (`chmod 700` / `600` on Unix) so they are not readable by other users on the same machine.

**No calendar event content is written to disk.** Events are fetched on demand and held in memory only for the duration of the `plan` or `validate` command.

## Third-party services

CalendarSync connects to the following services on your behalf:

- **Microsoft Graph API** — to access Microsoft 365 / Outlook calendars
- **Google Calendar API** — to access Google Workspace or personal Gmail calendars

Your use of these services is governed by their respective privacy policies:
- [Microsoft Privacy Statement](https://privacy.microsoft.com/en-us/privacystatement)
- [Google Privacy Policy](https://policies.google.com/privacy)

## OAuth credentials

### Microsoft
OAuth tokens are obtained via the Microsoft identity platform using your own registered Azure application. Tokens are cached locally using the MSAL token cache.

### Google Workspace
OAuth tokens are obtained using OAuth credentials you supply (client ID and secret from your own Google Cloud project). Tokens are cached locally via `Google.Apis` `FileDataStore`.

### Personal Gmail (`google-personal`)
This account type uses bundled OAuth credentials for the "installed application" flow. Per [Google's documentation](https://developers.google.com/identity/protocols/oauth2/native-app#overview), the client secret for installed/desktop applications is not considered confidential and cannot be kept secret in a distributed binary. The credentials identify the application to Google; they do not grant any access to your data — that requires your explicit consent through the OAuth login flow.

## Data retention and deletion

- To revoke access, run `calendarsync account logout <id>`. This revokes your token with the provider and deletes the local token file.
- To remove an account entirely, run `calendarsync account remove <id>`.
- To delete all local data, remove `~/.calendarsync/`.

## Changes to this policy

This policy may be updated as the application evolves. The effective date at the top of this document reflects the most recent revision. Changes are tracked in the [commit history](https://github.com/tsolbjor/calendarsync/commits/main/PRIVACY.md).

## Contact

If you have questions about this policy, open an issue at [github.com/tsolbjor/calendarsync](https://github.com/tsolbjor/calendarsync/issues).
