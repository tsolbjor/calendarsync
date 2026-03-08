using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using CalendarSync.Cli.Config;

namespace CalendarSync.Cli.Providers;

public class GoogleCalendarProvider : ICalendarProvider
{
    private static readonly string[] Scopes = [CalendarService.Scope.Calendar];
    private readonly Dictionary<string, UserCredential> _credentials = [];

    public virtual CalendarProvider ProviderType => CalendarProvider.Google;
    public bool IsReadOnly => false;

    public async Task LoginAsync(AccountConfig account, CancellationToken ct = default)
    {
        var credential = await AuthorizeAsync(account, ct);
        _credentials[account.Id] = credential;
        Console.WriteLine($"Signed in to Google as {credential.UserId}");
    }

    public async Task LogoutAsync(AccountConfig account, CancellationToken ct = default)
    {
        if (_credentials.TryGetValue(account.Id, out var credential))
        {
            await credential.RevokeTokenAsync(ct);
            _credentials.Remove(account.Id);
        }

        // Also delete the persisted token file
        var tokenPath = GetTokenPath(account);
        if (File.Exists(tokenPath)) File.Delete(tokenPath);
    }

    public async Task<bool> IsAuthenticatedAsync(AccountConfig account, CancellationToken ct = default)
    {
        try
        {
            var flow = BuildFlow(account);
            var tokenResponse = await flow.LoadTokenAsync(account.Id, ct);
            if (tokenResponse is null) return false;

            // Refresh if expired
            if (tokenResponse.IsStale)
                tokenResponse = await flow.RefreshTokenAsync(account.Id, tokenResponse.RefreshToken, ct);

            return tokenResponse is not null;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(AccountConfig account, CancellationToken ct = default)
    {
        var service = await GetCalendarServiceAsync(account, ct);
        var list = await service.CalendarList.List().ExecuteAsync(ct);
        return list.Items?
            .Select(c => new CalendarInfo(c.Id, c.Summary, c.BackgroundColor))
            .ToList() ?? [];
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(AccountConfig account, string calendarId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var service = await GetCalendarServiceAsync(account, ct);
        var request = service.Events.List(calendarId);
        request.TimeMinDateTimeOffset = from;
        request.TimeMaxDateTimeOffset = to;
        request.SingleEvents = true;
        request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;

        var result = await request.ExecuteAsync(ct);
        return result.Items?
            .Select(e => new CalendarEvent(
                Id: e.Id,
                CalendarId: calendarId,
                Subject: e.Summary ?? "(No subject)",
                Start: e.Start.DateTimeDateTimeOffset ?? DateTimeOffset.Parse(e.Start.Date!),
                End: e.End.DateTimeDateTimeOffset ?? DateTimeOffset.Parse(e.End.Date!),
                IsAllDay: e.Start.Date is not null,
                Location: e.Location,
                BodyPreview: e.Description is { Length: > 0 } d ? d[..Math.Min(200, d.Length)] : null))
            .ToList() ?? [];
    }

    public async Task<string> CreateEventAsync(AccountConfig account, string calendarId, NewEvent evt, CancellationToken ct = default)
    {
        var service = await GetCalendarServiceAsync(account, ct);
        var created = await service.Events.Insert(ToGoogleEvent(evt), calendarId).ExecuteAsync(ct);
        return created.Id;
    }

    public async Task UpdateEventAsync(AccountConfig account, string calendarId, string eventId, NewEvent evt, CancellationToken ct = default)
    {
        var service = await GetCalendarServiceAsync(account, ct);
        await service.Events.Update(ToGoogleEvent(evt), calendarId, eventId).ExecuteAsync(ct);
    }

    public async Task DeleteEventAsync(AccountConfig account, string calendarId, string eventId, CancellationToken ct = default)
    {
        var service = await GetCalendarServiceAsync(account, ct);
        await service.Events.Delete(calendarId, eventId).ExecuteAsync(ct);
    }

    private static Google.Apis.Calendar.v3.Data.Event ToGoogleEvent(NewEvent evt) => new()
    {
        Summary = evt.Subject,
        Description = evt.Body,
        // ColorId 9 = Blueberry — visually distinct, works for a placeholder/busy block
        ColorId = "9",
        Start = evt.IsAllDay
            ? new Google.Apis.Calendar.v3.Data.EventDateTime { Date = evt.Start.ToString("yyyy-MM-dd") }
            : new Google.Apis.Calendar.v3.Data.EventDateTime { DateTimeDateTimeOffset = evt.Start },
        End = evt.IsAllDay
            ? new Google.Apis.Calendar.v3.Data.EventDateTime { Date = evt.End.ToString("yyyy-MM-dd") }
            : new Google.Apis.Calendar.v3.Data.EventDateTime { DateTimeDateTimeOffset = evt.End }
    };

    private async Task<CalendarService> GetCalendarServiceAsync(AccountConfig account, CancellationToken ct)
    {
        if (!_credentials.TryGetValue(account.Id, out var credential))
            credential = await AuthorizeAsync(account, ct);

        return new CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "CalendarSync"
        });
    }

    protected virtual ClientSecrets GetClientSecrets(AccountConfig account)
    {
        if (string.IsNullOrEmpty(account.GoogleClientId) || string.IsNullOrEmpty(account.GoogleClientSecret))
            throw new InvalidOperationException(
                $"Google account '{account.Id}' is missing OAuth credentials. " +
                "Re-add it with: account add google --id <id> --client-id <id> --client-secret <secret>");

        return new ClientSecrets
        {
            ClientId = account.GoogleClientId,
            ClientSecret = account.GoogleClientSecret
        };
    }

    private async Task<UserCredential> AuthorizeAsync(AccountConfig account, CancellationToken ct)
    {
        var clientSecrets = GetClientSecrets(account);

        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            clientSecrets,
            Scopes,
            account.Id,
            ct,
            new FileDataStore(Config.ConfigManager.TokenCacheDir, fullPath: true),
            new PrintUrlCodeReceiver());

        _credentials[account.Id] = credential;
        return credential;
    }

    private GoogleAuthorizationCodeFlow BuildFlow(AccountConfig account)
    {
        return new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = GetClientSecrets(account),
            Scopes = Scopes,
            DataStore = new FileDataStore(Config.ConfigManager.TokenCacheDir, fullPath: true)
        });
    }

    private static string GetTokenPath(AccountConfig account) =>
        Path.Combine(Config.ConfigManager.TokenCacheDir, $"Google.Apis.Auth.OAuth2.Responses.TokenResponse-{account.Id}");
}

/// <summary>
/// OAuth code receiver that prints the authorization URL to the console and listens on
/// localhost for the callback. Works in WSL/headless environments — open the URL in any
/// browser; WSL2 forwards localhost ports from Windows automatically.
/// </summary>
internal class PrintUrlCodeReceiver : ICodeReceiver
{
    public string RedirectUri { get; } = $"http://127.0.0.1:{GetFreePort()}/authorize/";

    public async Task<AuthorizationCodeResponseUrl> ReceiveCodeAsync(
        AuthorizationCodeRequestUrl url,
        CancellationToken ct)
    {
        url.RedirectUri = RedirectUri;
        var authUrl = url.Build().AbsoluteUri;

        Console.WriteLine();
        Console.WriteLine("Open this URL in your browser to sign in to Google:");
        Console.WriteLine(authUrl);
        Console.WriteLine();
        Console.WriteLine($"Waiting for authorization on {RedirectUri} ...");

        using var listener = new System.Net.HttpListener();
        listener.Prefixes.Add(RedirectUri);
        listener.Start();

        var context = await listener.GetContextAsync().WaitAsync(ct);

        const string html = "<html><body><h1>Authorization complete — you can close this tab.</h1></body></html>";
        var bytes = System.Text.Encoding.UTF8.GetBytes(html);
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, ct);
        context.Response.OutputStream.Close();

        var query = context.Request.Url?.Query?.TrimStart('?') ?? string.Empty;
        var pairs = query
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));

        return new AuthorizationCodeResponseUrl
        {
            Code = pairs.GetValueOrDefault("code"),
            Error = pairs.GetValueOrDefault("error"),
            State = pairs.GetValueOrDefault("state"),
        };
    }

    private static int GetFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
