using Microsoft.Identity.Client;
using Microsoft.Graph;
using Azure.Core;
using CalendarSync.Cli.Config;

namespace CalendarSync.Cli.Providers;

public class MicrosoftCalendarProvider : ICalendarProvider
{
    private static readonly string[] Scopes = ["Calendars.ReadWrite", "User.Read"];

    private readonly Dictionary<string, IPublicClientApplication> _apps = [];

    public CalendarProvider ProviderType => CalendarProvider.Microsoft;
    public bool IsReadOnly => false;

    public async Task LoginAsync(AccountConfig account, CancellationToken ct = default)
    {
        var app = await GetOrCreateAppAsync(account);
        var accounts = await app.GetAccountsAsync();

        try
        {
            await app.AcquireTokenSilent(Scopes, accounts.FirstOrDefault()).ExecuteAsync(ct);
            return;
        }
        catch (MsalUiRequiredException) { }

        if (account.UseDeviceCode)
        {
            await AcquireViaDeviceCodeAsync(app, Scopes, ct);
            return;
        }

        // Primary: interactive browser / PKCE — no client secret required
        try
        {
            await app.AcquireTokenInteractive(Scopes)
                .WithUseEmbeddedWebView(false)
                .ExecuteAsync(ct);
            return;
        }
        catch (MsalClientException ex) when (ex.ErrorCode is "authentication_ui_failed" or "browser_not_supported")
        {
            Console.WriteLine("[No browser available, falling back to device code flow]");
        }

        await AcquireViaDeviceCodeAsync(app, Scopes, ct);
    }

    public async Task LogoutAsync(AccountConfig account, CancellationToken ct = default)
    {
        var app = await GetOrCreateAppAsync(account);
        foreach (var msalAccount in await app.GetAccountsAsync())
            await app.RemoveAsync(msalAccount);
    }

    public async Task<bool> IsAuthenticatedAsync(AccountConfig account, CancellationToken ct = default)
    {
        var app = await GetOrCreateAppAsync(account);
        var accounts = await app.GetAccountsAsync();
        if (!accounts.Any()) return false;

        try
        {
            await app.AcquireTokenSilent(Scopes, accounts.First()).ExecuteAsync(ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(AccountConfig account, CancellationToken ct = default)
    {
        var client = await GetGraphClientAsync(account);
        var result = await client.Me.Calendars.GetAsync(cancellationToken: ct);
        return result?.Value?
            .Select(c => new CalendarInfo(c.Id!, c.Name!, c.HexColor))
            .ToList() ?? [];
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(AccountConfig account, string calendarId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var client = await GetGraphClientAsync(account);
        var result = await client.Me.Calendars[calendarId].CalendarView.GetAsync(req =>
        {
            req.QueryParameters.StartDateTime = from.UtcDateTime.ToString("O");
            req.QueryParameters.EndDateTime = to.UtcDateTime.ToString("O");
        }, ct);

        return result?.Value?
            .Select(e => new CalendarEvent(
                Id: e.Id!,
                CalendarId: calendarId,
                Subject: e.Subject ?? "(No subject)",
                Start: ParseGraphDateTime(e.Start) ?? from,
                End: ParseGraphDateTime(e.End) ?? to,
                IsAllDay: e.IsAllDay ?? false,
                Location: e.Location?.DisplayName,
                BodyPreview: e.BodyPreview))
            .ToList() ?? [];
    }

    public async Task<string> CreateEventAsync(AccountConfig account, string calendarId, NewEvent evt, CancellationToken ct = default)
    {
        var client = await GetGraphClientAsync(account);
        var created = await client.Me.Calendars[calendarId].Events.PostAsync(ToGraphEvent(evt), cancellationToken: ct);
        return created!.Id!;
    }

    public async Task UpdateEventAsync(AccountConfig account, string calendarId, string eventId, NewEvent evt, CancellationToken ct = default)
    {
        var client = await GetGraphClientAsync(account);
        await client.Me.Events[eventId].PatchAsync(ToGraphEvent(evt), cancellationToken: ct);
    }

    public async Task DeleteEventAsync(AccountConfig account, string calendarId, string eventId, CancellationToken ct = default)
    {
        var client = await GetGraphClientAsync(account);
        await client.Me.Events[eventId].DeleteAsync(cancellationToken: ct);
    }

    private static Microsoft.Graph.Models.Event ToGraphEvent(NewEvent evt) => new()
    {
        Subject = evt.Subject,
        Body = new Microsoft.Graph.Models.ItemBody
        {
            Content = evt.Body,
            ContentType = Microsoft.Graph.Models.BodyType.Text
        },
        Start = new Microsoft.Graph.Models.DateTimeTimeZone
        {
            DateTime = evt.Start.UtcDateTime.ToString("O"),
            TimeZone = "UTC"
        },
        End = new Microsoft.Graph.Models.DateTimeTimeZone
        {
            DateTime = evt.End.UtcDateTime.ToString("O"),
            TimeZone = "UTC"
        },
        IsAllDay = evt.IsAllDay,
        Categories = ["CalSync"]
    };

    public async Task<GraphServiceClient> GetGraphClientAsync(AccountConfig account)
    {
        var app = await GetOrCreateAppAsync(account);
        var provider = new MsalTokenProvider(app, Scopes);
        return new GraphServiceClient(new TokenCredentialAdapter(provider), Scopes);
    }

    private static async Task AcquireViaDeviceCodeAsync(IPublicClientApplication app, string[] scopes, CancellationToken ct)
    {
        await app.AcquireTokenWithDeviceCode(scopes, deviceCodeResult =>
        {
            Console.WriteLine();
            Console.WriteLine(deviceCodeResult.Message);
            Console.WriteLine();
            return Task.CompletedTask;
        }).ExecuteAsync(ct);
    }

    private async Task<IPublicClientApplication> GetOrCreateAppAsync(AccountConfig account)
    {
        if (_apps.TryGetValue(account.Id, out var existing))
            return existing;

        var app = PublicClientApplicationBuilder
            .Create(account.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, account.TenantId)
            .WithRedirectUri("http://localhost")
            .Build();

        RegisterFileCache(app, account.TenantId!);
        _apps[account.Id] = app;
        return app;
    }

    private static void RegisterFileCache(IPublicClientApplication app, string tenantId)
    {
        var cacheDir = Config.ConfigManager.TokenCacheDir;
        var cachePath = Path.Combine(cacheDir, $"msal_{tenantId}.cache");

        app.UserTokenCache.SetBeforeAccess(args =>
        {
            if (File.Exists(cachePath))
                args.TokenCache.DeserializeMsalV3(File.ReadAllBytes(cachePath));
        });

        app.UserTokenCache.SetAfterAccess(args =>
        {
            if (!args.HasStateChanged) return;
            Directory.CreateDirectory(cacheDir);
            File.WriteAllBytes(cachePath, args.TokenCache.SerializeMsalV3());
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(cachePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        });
    }

    private static DateTimeOffset? ParseGraphDateTime(Microsoft.Graph.Models.DateTimeTimeZone? dt)
    {
        if (dt?.DateTime is null) return null;
        var parsed = DateTimeOffset.Parse(dt.DateTime);
        if (dt.TimeZone is "UTC" or null)
            return new DateTimeOffset(parsed.DateTime, TimeSpan.Zero);
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(dt.TimeZone);
            return TimeZoneInfo.ConvertTime(parsed, tz);
        }
        catch
        {
            return parsed;
        }
    }
}

internal class MsalTokenProvider(IPublicClientApplication app, string[] scopes)
{
    public async Task<string> GetTokenAsync()
    {
        var accounts = await app.GetAccountsAsync();
        try
        {
            var result = await app.AcquireTokenSilent(scopes, accounts.FirstOrDefault()).ExecuteAsync();
            return result.AccessToken;
        }
        catch (MsalUiRequiredException) { }

        try
        {
            var result = await app.AcquireTokenInteractive(scopes)
                .WithUseEmbeddedWebView(false)
                .ExecuteAsync();
            return result.AccessToken;
        }
        catch (MsalClientException ex) when (ex.ErrorCode is "authentication_ui_failed" or "browser_not_supported")
        {
            Console.WriteLine("[No browser available, falling back to device code flow]");
        }

        var fallback = await app.AcquireTokenWithDeviceCode(scopes, cb =>
        {
            Console.WriteLine(cb.Message);
            return Task.CompletedTask;
        }).ExecuteAsync();
        return fallback.AccessToken;
    }
}

internal class TokenCredentialAdapter(MsalTokenProvider provider) : TokenCredential
{
    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        => GetTokenAsync(requestContext, cancellationToken).GetAwaiter().GetResult();

    public override async ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        var token = await provider.GetTokenAsync();
        return new AccessToken(token, DateTimeOffset.UtcNow.AddHours(1));
    }
}
