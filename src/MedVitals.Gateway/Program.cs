using MedVitals.Gateway.Authentication;
using MedVitals.Gateway.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// services to the container.
builder.Services.AddControllers();

// AnalyticsEngine:BaseUrl comes from appsettings.json (local default) or the
// AnalyticsEngine__BaseUrl environment variable (docker-compose); validated at startup
builder.Services.AddOptions<AnalyticsEngineOptions>()
    .Bind(builder.Configuration.GetSection(AnalyticsEngineOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddHttpClient<AnalyticsService>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<AnalyticsEngineOptions>>().Value;
    // trailing slash so relative paths ("api/risk/assess") append instead of replacing the last segment
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    // fail fast instead of hanging requests when the analytics engine is down
    client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
});
builder.Services.AddSingleton<LatestAssessmentStore>();

// ---------- CORS: allow the browser dashboard's origin(s) ----------
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(allowedOrigins)
        .WithMethods("GET", "POST")
        .WithHeaders("Content-Type", ApiKeyDefaults.HeaderName));
});

// ---------- Authentication / authorization (API keys) ----------
var apiKeys = builder.Configuration.GetSection(ApiKeyOptions.SectionName).Get<ApiKeyOptions>() ?? new ApiKeyOptions();
var isDevelopment = builder.Environment.IsDevelopment();
if (!isDevelopment && !apiKeys.IsConfigured)
{
    // secure by default: refuse to start wide open outside Development
    throw new InvalidOperationException(
        "Auth:DeviceApiKey and Auth:DashboardApiKey must be configured outside Development " +
        "(env: Auth__DeviceApiKey / Auth__DashboardApiKey).");
}
// only in Development with no keys at all is the API left open (for local tinkering)
var enforceAuth = !isDevelopment
    || !string.IsNullOrWhiteSpace(apiKeys.DeviceApiKey)
    || !string.IsNullOrWhiteSpace(apiKeys.DashboardApiKey);

builder.Services.Configure<ApiKeyOptions>(builder.Configuration.GetSection(ApiKeyOptions.SectionName));
builder.Services.AddAuthentication(ApiKeyDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyDefaults.Scheme, null);

builder.Services.AddAuthorization(options =>
{
    if (enforceAuth)
    {
        options.AddPolicy(ApiKeyDefaults.DevicePolicy, p => p.RequireRole(ApiKeyDefaults.DeviceRole));
        options.AddPolicy(ApiKeyDefaults.DashboardPolicy, p => p.RequireRole(ApiKeyDefaults.DashboardRole));
        // any endpoint without an explicit policy still requires an authenticated caller
        options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    }
    else
    {
        options.AddPolicy(ApiKeyDefaults.DevicePolicy, p => p.RequireAssertion(_ => true));
        options.AddPolicy(ApiKeyDefaults.DashboardPolicy, p => p.RequireAssertion(_ => true));
    }
});

// ---------- HTTPS ----------
// TLS is terminated by a reverse proxy in front of the container. Set
// ASPNETCORE_FORWARDEDHEADERS_ENABLED=true so X-Forwarded-Proto is honoured, and
// ASPNETCORE_HTTPS_PORT (e.g. 443) to enable redirection; without an HTTPS port the
// redirection middleware is a no-op, so plain-HTTP devices on the LAN keep working.
builder.Services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(180));

// learn more about configuring Swagger/OpenAPI https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (!enforceAuth)
{
    app.Logger.LogWarning("Auth keys not configured: API is UNAUTHENTICATED (Development only).");
}
if (allowedOrigins.Length == 0)
{
    app.Logger.LogWarning("Cors:AllowedOrigins is empty: browser dashboards on other origins will be blocked.");
}

// configure HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHsts();
}

app.UseHttpsRedirection();

// CORS must run before auth so preflight (OPTIONS) requests are answered without an API key
app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// exposed for WebApplicationFactory integration tests
public partial class Program { }
