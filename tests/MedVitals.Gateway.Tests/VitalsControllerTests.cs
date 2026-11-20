using System.Net;
using System.Text;
using MedVitals.Gateway.Controllers;
using MedVitals.Gateway.Models;
using MedVitals.Gateway.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MedVitals.Gateway.Tests;

public class VitalsControllerTests
{
    /// <summary>Fake handler that returns a canned response (or throws) and records requests.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public List<Uri> Requests { get; } = new();

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public StubHandler(HttpStatusCode status, string body)
            : this(_ => new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            }) { }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(_respond(request));
        }
    }

    private const string GreenJson =
        "{\"patientId\":\"P-1\",\"bedNumber\":\"B-1\",\"mewsScore\":0,\"triageLevel\":\"GREEN\",\"recommendation\":\"STABLE\",\"aiRiskScore\":0.12,\"modelVersion\":\"v1\"}";

    private static (VitalsController controller, StubHandler handler, LatestAssessmentStore store) Create(StubHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://analytics.test/") };
        var service = new AnalyticsService(client, NullLogger<AnalyticsService>.Instance);
        var store = new LatestAssessmentStore();
        return (new VitalsController(service, store), handler, store);
    }

    private static PatientVitals SampleVitals(string id = "P-1") => new()
    {
        PatientId = id,
        BedNumber = "B-1",
        HeartRate = 75,
        RespiratoryRate = 12,
        SystolicBp = 120,
        TemperatureCelsius = 37.0,
        OxygenSaturation = 98
    };

    [Fact]
    public async Task AssessVitals_ReturnsOkAndCachesLatest()
    {
        var (controller, handler, _) = Create(new StubHandler(HttpStatusCode.OK, GreenJson));

        var action = await controller.AssessVitals(SampleVitals(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var result = Assert.IsType<RiskAssessmentResult>(ok.Value);
        Assert.Equal("GREEN", result.TriageLevel);
        Assert.Equal(0.12f, result.AiRiskScore);
        Assert.Single(handler.Requests);
        Assert.EndsWith("/api/risk/assess", handler.Requests[0].AbsolutePath);

        var latest = Assert.IsType<OkObjectResult>(controller.GetLatest("P-1").Result);
        Assert.Same(result, latest.Value);
    }

    [Fact]
    public void GetLatest_UnknownPatient_ReturnsNotFound()
    {
        var (controller, _, _) = Create(new StubHandler(HttpStatusCode.OK, GreenJson));

        Assert.IsType<NotFoundResult>(controller.GetLatest("NOBODY").Result);
    }

    [Fact]
    public async Task AssessVitalBatch_EmptyBatch_ReturnsBadRequest()
    {
        var (controller, handler, _) = Create(new StubHandler(HttpStatusCode.OK, "[]"));

        var action = await controller.AssessVitalBatch(new List<PatientVitals>(), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(action.Result);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AssessVitalBatch_UsesSingleBatchCall()
    {
        var (controller, handler, _) = Create(new StubHandler(HttpStatusCode.OK, $"[{GreenJson},{GreenJson},{GreenJson}]"));
        var batch = new List<PatientVitals> { SampleVitals(), SampleVitals(), SampleVitals() };

        var action = await controller.AssessVitalBatch(batch, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var results = Assert.IsType<List<RiskAssessmentResult>>(ok.Value);
        Assert.Equal(3, results.Count);
        Assert.Single(handler.Requests);
        Assert.EndsWith("/api/risk/batch-assess", handler.Requests[0].AbsolutePath);
    }

    [Fact]
    public async Task AssessVitals_ForwardsHistoricalVitalsInJsonBody()
    {
        string? capturedBody = null;
        var handler = new StubHandler(req =>
        {
            capturedBody = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(GreenJson, Encoding.UTF8, "application/json")
            };
        });
        var (controller, _, _) = Create(handler);
        var vitals = SampleVitals();
        vitals.HistoricalVitals = new List<float[]> { new[] { 72f, 120f, 12f, 36.8f, 98f } };

        await controller.AssessVitals(vitals, CancellationToken.None);

        Assert.NotNull(capturedBody);
        Assert.Contains("\"historicalVitals\":[[72,120,12,36.8,98]]", capturedBody);
    }

    [Fact]
    public async Task AssessVitals_MissingAllVitalsWhenSignalValid_RejectedByModelValidation()
    {
        await using var factory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("AnalyticsEngine:BaseUrl", "http://analytics-engine:8080");
                b.UseSetting("Auth:DeviceApiKey", "k1");
                b.UseSetting("Auth:DashboardApiKey", "k2");
            });
        var client = factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/vitals")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { patientId = "P-1", bedNumber = "B-1" })
        };
        req.Headers.Add("X-Api-Key", "k1");

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task AssessVitals_OutOfRangeSpo2_RejectedByModelValidation()
    {
        await using var factory = new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>()
            .WithWebHostBuilder(b =>
            {
                b.UseSetting("AnalyticsEngine:BaseUrl", "http://analytics-engine:8080");
                b.UseSetting("Auth:DeviceApiKey", "k1");
                b.UseSetting("Auth:DashboardApiKey", "k2");
            });
        var client = factory.CreateClient();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/vitals")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { patientId = "P-1", oxygenSaturation = 0 })
        };
        req.Headers.Add("X-Api-Key", "k1");

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task AssessVitals_AnalyticsErrorStatus_Returns502()
    {
        var (controller, _, _) = Create(new StubHandler(HttpStatusCode.InternalServerError, "{}"));

        var action = await controller.AssessVitals(SampleVitals(), CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(StatusCodes.Status502BadGateway, problem.StatusCode);
    }

    [Fact]
    public async Task AssessVitals_AnalyticsRejectsPayload_Returns400()
    {
        var (controller, _, _) = Create(new StubHandler(HttpStatusCode.BadRequest, "{}"));

        var action = await controller.AssessVitals(SampleVitals(), CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    public void HistoricalVitals_WrongRowLength_FailsValidation(int rowLength)
    {
        var vitals = SampleVitals();
        vitals.HistoricalVitals = new List<float[]> { new float[rowLength] };

        var errors = vitals.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(vitals)).ToList();

        Assert.Contains(errors, e => e.ErrorMessage!.Contains("exactly 5 values"));
    }

    [Fact]
    public void HistoricalVitals_NonFinite_FailsValidation()
    {
        var vitals = SampleVitals();
        vitals.HistoricalVitals = new List<float[]> { new[] { 75f, 120f, float.NaN, 37f, 98f } };

        var errors = vitals.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(vitals)).ToList();

        Assert.Contains(errors, e => e.ErrorMessage!.Contains("finite"));
    }

    [Fact]
    public async Task AssessVitals_AnalyticsUnreachable_Returns503()
    {
        var (controller, _, _) = Create(new StubHandler(_ => throw new HttpRequestException("connection refused")));

        var action = await controller.AssessVitals(SampleVitals(), CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.StatusCode);
    }

    [Fact]
    public async Task AssessVitals_AnalyticsTimeout_Returns503()
    {
        var (controller, _, _) = Create(new StubHandler(_ => throw new TaskCanceledException("timeout")));

        var action = await controller.AssessVitals(SampleVitals(), CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.StatusCode);
    }

    [Fact]
    public async Task AssessVitalBatch_AnalyticsUnreachable_Returns503()
    {
        var (controller, _, _) = Create(new StubHandler(_ => throw new HttpRequestException("connection refused")));

        var action = await controller.AssessVitalBatch(new List<PatientVitals> { SampleVitals() }, CancellationToken.None);

        var problem = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, problem.StatusCode);
    }
}
