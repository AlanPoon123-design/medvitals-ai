using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace MedVitals.Gateway.Authentication;

/// <summary>Bound from the "Auth" config section (env: Auth__DeviceApiKey, Auth__DashboardApiKey).</summary>
public class ApiKeyOptions
{
    public const string SectionName = "Auth";

    /// <summary>Shared secret for ESP32 telemetry devices (role: Device).</summary>
    public string DeviceApiKey { get; set; } = string.Empty;

    /// <summary>Key for the dashboard (role: Dashboard).</summary>
    public string DashboardApiKey { get; set; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(DeviceApiKey) && !string.IsNullOrWhiteSpace(DashboardApiKey);
}

public static class ApiKeyDefaults
{
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";
    public const string DeviceRole = "Device";
    public const string DashboardRole = "Dashboard";
    public const string DevicePolicy = "DevicePolicy";
    public const string DashboardPolicy = "DashboardPolicy";
}

/// <summary>
/// Authenticates requests carrying an <c>X-Api-Key</c> header and assigns the Device or Dashboard
/// role depending on which configured key matches (constant-time comparison).
/// </summary>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IOptionsMonitor<ApiKeyOptions> _keys;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptionsMonitor<ApiKeyOptions> keys)
        : base(options, logger, encoder)
    {
        _keys = keys;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyDefaults.HeaderName, out var values) || string.IsNullOrEmpty(values.ToString()))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var provided = values.ToString();
        var keys = _keys.CurrentValue;

        string? role = null;
        if (Matches(provided, keys.DeviceApiKey)) role = ApiKeyDefaults.DeviceRole;
        else if (Matches(provided, keys.DashboardApiKey)) role = ApiKeyDefaults.DashboardRole;

        if (role is null)
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        }

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, role.ToLowerInvariant()),
            new Claim(ClaimTypes.Role, role)
        }, ApiKeyDefaults.Scheme);

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), ApiKeyDefaults.Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static bool Matches(string provided, string expected)
    {
        if (string.IsNullOrEmpty(expected)) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));
    }
}
