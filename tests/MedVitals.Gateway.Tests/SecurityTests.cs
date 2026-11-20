using System.Net;
using System.Net.Http.Json;
using System.Text;
using MedVitals.Gateway.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedVitals.Gateway.Tests;

/// <summary>Boots the real pipeline to verify API-key auth, CORS and secure-by-default startup.</summary>
public class SecurityTests
{
    private const string DeviceKey = "device-secret";
    private const string DashboardKey = "dashboard-secret";
    private const string AllowedOrigin = "http://dashboard.test";

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.RequestUri!.AbsolutePath.EndsWith("batch-assess")
                ? "[{\"patientId\":\"P-1\",\"triageLevel\":\"GREEN\"}]"
                : "{\"patientId\":\"P-1\",\"triageLevel\":\"GREEN\"}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string environment = "Production", string? deviceKey = DeviceKey, string? dashboardKey = DashboardKey) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("AnalyticsEngine:BaseUrl", "http://analytics-engine:8080");
            builder.UseSetting("Auth:DeviceApiKey", deviceKey);
            builder.UseSetting("Auth:DashboardApiKey", dashboardKey);
            builder.UseSetting("Cors:AllowedOrigins:0", AllowedOrigin);
            builder.ConfigureServices(services =>
                services.AddHttpClient<AnalyticsService>().ConfigurePrimaryHttpMessageHandler(() => new OkHandler()));
        });

    private static object Vitals() => new
    {
        patientId = "P-1", bedNumber = "B-1", heartRate = 75, respiratoryRate = 12,
        systolicBp = 120, temperatureCelsius = 37.0, oxygenSaturation = 98
    };

    private static HttpRequestMessage Post(string url, object body, string? key)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (key is not null) req.Headers.Add("X-Api-Key", key);
        return req;
    }

    private static HttpRequestMessage Get(string url, string? key)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (key is not null) req.Headers.Add("X-Api-Key", key);
        return req;
    }

    // ---------- authentication / authorization ----------

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]          // no key
    [InlineData("wrong", HttpStatusCode.Unauthorized)]       // unknown key
    [InlineData(DashboardKey, HttpStatusCode.Forbidden)]     // valid key, wrong role
    [InlineData(DeviceKey, HttpStatusCode.OK)]
    public async Task DeviceEndpoint_RequiresDeviceKey(string? key, HttpStatusCode expected)
    {
        await using var factory = CreateFactory();
        var res = await factory.CreateClient().SendAsync(Post("/api/vitals", Vitals(), key));
        Assert.Equal(expected, res.StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(DeviceKey, HttpStatusCode.Forbidden)]
    [InlineData(DashboardKey, HttpStatusCode.OK)]
    public async Task BatchEndpoint_RequiresDashboardKey(string? key, HttpStatusCode expected)
    {
        await using var factory = CreateFactory();
        var res = await factory.CreateClient().SendAsync(Post("/api/vitals/batch-assess", new[] { Vitals() }, key));
        Assert.Equal(expected, res.StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(DeviceKey, HttpStatusCode.Forbidden)]
    [InlineData(DashboardKey, HttpStatusCode.NotFound)] // authorised; no reading cached yet
    public async Task LatestEndpoint_RequiresDashboardKey(string? key, HttpStatusCode expected)
    {
        await using var factory = CreateFactory();
        var res = await factory.CreateClient().SendAsync(Get("/api/vitals/P-1", key));
        Assert.Equal(expected, res.StatusCode);
    }

    [Fact]
    public async Task DeviceReading_IsVisibleToDashboard()
    {
        await using var factory = CreateFactory();
        var client = factory.CreateClient();

        (await client.SendAsync(Post("/api/vitals", Vitals(), DeviceKey))).EnsureSuccessStatusCode();
        var res = await client.SendAsync(Get("/api/vitals/P-1", DashboardKey));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    // ---------- secure by default ----------

    [Theory]
    [InlineData(null, DashboardKey)]
    [InlineData(DeviceKey, null)]
    [InlineData(null, null)]
    public void Production_WithoutKeys_FailsToStart(string? deviceKey, string? dashboardKey)
    {
        using var factory = CreateFactory("Production", deviceKey, dashboardKey);
        var ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("Auth:DeviceApiKey", ex.ToString());
    }

    [Fact]
    public async Task Development_WithoutKeys_IsOpen()
    {
        await using var factory = CreateFactory("Development", null, null);
        var res = await factory.CreateClient().SendAsync(Post("/api/vitals", Vitals(), key: null));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    // ---------- CORS ----------

    private static HttpRequestMessage Preflight(string origin)
    {
        var req = new HttpRequestMessage(HttpMethod.Options, "/api/vitals/batch-assess");
        req.Headers.Add("Origin", origin);
        req.Headers.Add("Access-Control-Request-Method", "POST");
        req.Headers.Add("Access-Control-Request-Headers", "content-type,x-api-key");
        return req;
    }

    [Fact]
    public async Task Preflight_FromAllowedOrigin_SucceedsWithoutApiKey()
    {
        await using var factory = CreateFactory();
        var res = await factory.CreateClient().SendAsync(Preflight(AllowedOrigin));

        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
        Assert.Equal(AllowedOrigin, Assert.Single(res.Headers.GetValues("Access-Control-Allow-Origin")));
        var allowedHeaders = string.Join(",", res.Headers.GetValues("Access-Control-Allow-Headers")).ToLowerInvariant();
        Assert.Contains("x-api-key", allowedHeaders);
    }

    [Fact]
    public async Task Preflight_FromOtherOrigin_IsNotAllowed()
    {
        await using var factory = CreateFactory();
        var res = await factory.CreateClient().SendAsync(Preflight("http://evil.test"));

        Assert.False(res.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task ActualRequest_FromAllowedOrigin_GetsCorsHeader()
    {
        await using var factory = CreateFactory();
        var req = Get("/api/vitals/P-1", DashboardKey);
        req.Headers.Add("Origin", AllowedOrigin);

        var res = await factory.CreateClient().SendAsync(req);

        Assert.Equal(AllowedOrigin, Assert.Single(res.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    // ---------- HTTPS ----------

    [Fact]
    public async Task Production_SendsHstsOverHttps()
    {
        await using var factory = CreateFactory();
        // HSTS is never sent for localhost by design, so use a real-looking host
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://gateway.test") });

        var res = await client.SendAsync(Get("/api/vitals/P-1", DashboardKey));

        Assert.True(res.Headers.Contains("Strict-Transport-Security"));
    }
}
