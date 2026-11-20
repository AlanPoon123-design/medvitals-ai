using System.ComponentModel.DataAnnotations;

namespace MedVitals.Gateway.Services;

/// <summary>Bound from the "AnalyticsEngine" config section (env: AnalyticsEngine__BaseUrl, AnalyticsEngine__TimeoutSeconds).</summary>
public class AnalyticsEngineOptions
{
    public const string SectionName = "AnalyticsEngine";

    [Required, Url]
    public string BaseUrl { get; set; } = string.Empty;

    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 10;
}
