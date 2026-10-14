using System.Net.Http.Json;
using MedVitals.Gateway.Models;

namespace MedVitals.Gateway.Services;

public class AnalyticsService{
    private readonly HttpClient _httpClient;

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
}