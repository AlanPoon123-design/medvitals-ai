using System.Net;
using System.Net.Http.Json;
using MedVitals.Gateway.Models;

namespace MedVitals.Gateway.Services;

/// <summary>
/// Raised when the analytics engine cannot be reached or returns an unusable response.
/// <see cref="StatusCode"/> is the status the gateway should surface to its caller
/// (503 when the engine is unreachable / timed out, 502 when it answered with an error).
/// </summary>
public class AnalyticsUnavailableException : Exception
{
    public HttpStatusCode StatusCode { get; }

    public AnalyticsUnavailableException(HttpStatusCode statusCode, string message, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}

public class AnalyticsService
{
    // relative to HttpClient.BaseAddress, which is set from AnalyticsEngine:BaseUrl in Program.cs
    private const string AssessPath = "api/risk/assess";
    private const string BatchAssessPath = "api/risk/batch-assess";

    private readonly HttpClient _httpClient;
    private readonly ILogger<AnalyticsService> _logger;

    public AnalyticsService(HttpClient httpClient, ILogger<AnalyticsService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public Task<RiskAssessmentResult> ForwardToAnalyticsAsync(PatientVitals vitals, CancellationToken ct = default)
        => PostAsync<PatientVitals, RiskAssessmentResult>(AssessPath, vitals, ct);

    public Task<List<RiskAssessmentResult>> AssessBatchAsync(List<PatientVitals> vitalsBatch, CancellationToken ct = default)
        => PostAsync<List<PatientVitals>, List<RiskAssessmentResult>>(BatchAssessPath, vitalsBatch, ct);

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest payload, CancellationToken ct)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync(path, payload, ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // HttpClient.Timeout elapsed (not a client-initiated cancel)
            _logger.LogWarning(ex, "Analytics engine timed out on {Path}", path);
            throw new AnalyticsUnavailableException(HttpStatusCode.ServiceUnavailable, "Analytics engine timed out.", ex);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Analytics engine unreachable on {Path}", path);
            throw new AnalyticsUnavailableException(HttpStatusCode.ServiceUnavailable, "Analytics engine is unreachable.", ex);
        }

        using (response)
        {
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                // the engine rejected the payload: that's a client error, not an outage
                _logger.LogInformation("Analytics engine rejected payload on {Path}", path);
                throw new AnalyticsUnavailableException(HttpStatusCode.BadRequest,
                    "The analytics engine rejected the vital-sign payload as invalid.");
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Analytics engine returned {StatusCode} on {Path}", (int)response.StatusCode, path);
                throw new AnalyticsUnavailableException(HttpStatusCode.BadGateway,
                    $"Analytics engine returned HTTP {(int)response.StatusCode}.");
            }

            try
            {
                var result = await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken: ct);
                return result ?? throw new AnalyticsUnavailableException(HttpStatusCode.BadGateway,
                    "Analytics engine returned an empty response.");
            }
            catch (System.Text.Json.JsonException ex)
            {
                _logger.LogWarning(ex, "Analytics engine returned malformed JSON on {Path}", path);
                throw new AnalyticsUnavailableException(HttpStatusCode.BadGateway,
                    "Analytics engine returned a malformed response.", ex);
            }
        }
    }
}
