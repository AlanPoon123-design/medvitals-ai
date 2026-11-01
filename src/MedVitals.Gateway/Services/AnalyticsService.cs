using System.Net.Http.Json;
using MedVitals.Gateway.Models;

namespace MedVitals.Gateway.Services;

public class AnalyticsService{
    private readonly HttpClient _httpClient;
    private readonly string _analyticsUrl = "http://localhost:8080/api/risk/assess";

    public AnalyticsService(HttpClient httpClient)
    {
    _httpClient = httpClient;
    }

    public async Task<List<RiskAssessmentResult>> AssessBatchAsync(List<PatientVitals> vitalsBatch)
    {
        var response = await _httpClient.PostAsJsonAsync("http://analytics-engine:8080/api/risk/assess", vitalsBatch);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<List<RiskAssessmentResult>>();
        return result ?? new List<RiskAssessmentResult>();
    }

    public async Task<RiskAssessmentResult> ForwardToAnalyticsAsync(PatientVitals vitals)
    {
        var response = await _httpClient.PostAsJsonAsync(_analyticsUrl, vitals);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<RiskAssessmentResult>();
        return result ?? new RiskAssessmentResult();
    }
}