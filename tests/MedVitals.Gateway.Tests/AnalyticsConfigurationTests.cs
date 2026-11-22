using System.Net;
using System.Net.Http.Json;
using System.Text;
using MedVitals.Gateway.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MedVitals.Gateway.Tests;

/// <summary>
/// Boots the real gateway pipeline (Program.cs) to verify AnalyticsEngine:BaseUrl is
/// actually used for outbound calls and is validated at startup.
/// </summary>
public class AnalyticsConfigurationTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            var body = request.RequestUri!.AbsolutePath.EndsWith("batch-assess")
                ? "[{\"patientId\":\"P-1\",\"triageLevel\":\"GREEN\"}]"
                : "{\"patientId\":\"P-1\",\"triageLevel\":\"GREEN\"}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(string? baseUrl, RecordingHandler handler) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // same key the AnalyticsEngine__BaseUrl env var in docker-compose maps to
            builder.UseSetting("AnalyticsEngine:BaseUrl", baseUrl);
            builder.ConfigureServices(services =>
                services.AddHttpClient<AnalyticsService>().ConfigurePrimaryHttpMessageHandler(() => handler));
        });

    private static object SampleVitals() => new
    {
        patientId = "P-1",
        bedNumber = "B-1",
        heartRate = 75,
        respiratoryRate = 12,
        systolicBp = 120,
        temperatureCelsius = 37.0,
        oxygenSaturation = 98
    };

    [Theory]
    [InlineData("http://analytics-engine:8080")]
    [InlineData("http://analytics-engine:8080/")]
    public async Task Assess_UsesConfiguredBaseUrl(string baseUrl)
    {
        var handler = new RecordingHandler();
        await using var factory = CreateFactory(baseUrl, handler);
        var client = factory.CreateClient();

        var res = await client.PostAsJsonAsync("/api/vitals", SampleVitals());

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(new Uri("http://analytics-engine:8080/api/risk/assess"), Assert.Single(handler.Requests));
    }

    [Fact]
    public async Task BatchAssess_UsesConfiguredBaseUrl()
    {
        var handler = new RecordingHandler();
        await using var factory = CreateFactory("http://analytics-engine:8080", handler);
        var client = factory.CreateClient();

        var res = await client.PostAsJsonAsync("/api/vitals/batch-assess", new[] { SampleVitals() });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(new Uri("http://analytics-engine:8080/api/risk/batch-assess"), Assert.Single(handler.Requests));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-url")]
    public void InvalidBaseUrl_FailsAtStartup(string baseUrl)
    {
        using var factory = CreateFactory(baseUrl, new RecordingHandler());

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }
}
