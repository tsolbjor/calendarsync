namespace CalendarSync.Cli.Config;

public enum CalendarProvider
{
    Microsoft,
    Google,
    GooglePersonal,
    ReadOnly
}

public class AccountConfig
{
    // Universal
    public required string Id { get; set; }
    public required CalendarProvider Provider { get; set; }
    public string? DisplayName { get; set; }

    // Microsoft (Entra/Graph)
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }

    /// <summary>
    /// When true, skip the interactive browser flow and use device code instead.
    /// Required when the browser cannot satisfy the tenant's MFA policy (e.g. WebAuthn in WSL).
    /// The app registration must have "Allow public client flows" enabled.
    /// </summary>
    public bool UseDeviceCode { get; set; }

    // Google Calendar
    public string? GoogleClientId { get; set; }
    public string? GoogleClientSecret { get; set; }

    // ReadOnly (ICS feed URL)
    public string? IcsUrl { get; set; }

    // Selected calendars (configured via 'setup')
    public List<SelectedCalendar> SelectedCalendars { get; set; } = [];
}

public class SelectedCalendar
{
    public required string Id { get; set; }
    public required string Name { get; set; }
}
