using System.Collections.Concurrent;
using MedVitals.Gateway.Models;

namespace MedVitals.Gateway.Services;

/// <summary>
/// In-memory cache of the most recent assessment per patient, so the dashboard can poll
/// GET api/vitals/{patientId} for readings pushed by ESP32 devices.
/// </summary>
public class LatestAssessmentStore
{
    private readonly ConcurrentDictionary<string, RiskAssessmentResult> _latest = new(StringComparer.OrdinalIgnoreCase);

    public void Set(RiskAssessmentResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.PatientId))
        {
            _latest[result.PatientId] = result;
        }
    }

    public bool TryGet(string patientId, out RiskAssessmentResult? result)
        => _latest.TryGetValue(patientId, out result);
}
