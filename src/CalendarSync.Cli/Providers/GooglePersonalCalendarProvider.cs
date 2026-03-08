using Google.Apis.Auth.OAuth2;
using CalendarSync.Cli.Config;

namespace CalendarSync.Cli.Providers;

/// <summary>
/// Google Calendar provider for personal (gmail.com) accounts.
/// Uses bundled OAuth credentials — no client ID or secret required from the user.
///
/// Security note: The client secret for an installed/desktop OAuth app is NOT confidential.
/// Google explicitly documents that it cannot be kept secret when distributed in a binary,
/// and that this is by design for the "installed application" OAuth flow:
/// https://developers.google.com/identity/protocols/oauth2/native-app#overview
///
/// Quoting the docs: "The client secret is not treated as a secret in this context."
/// The secret is only used to identify the application to Google, not to protect user data.
/// User tokens are stored separately in ~/.calendarsync/tokens/ and are owner-readable only.
/// </summary>
public class GooglePersonalCalendarProvider : GoogleCalendarProvider
{
    public override CalendarProvider ProviderType => CalendarProvider.GooglePersonal;

    protected override ClientSecrets GetClientSecrets(AccountConfig _) => new()
    {
        ClientId = "220173633859-lkgr56iudf7o8cihap7323321rntul1i.apps.googleusercontent.com",
        ClientSecret = "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"
    };
}
