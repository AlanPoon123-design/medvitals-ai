namespace MedVitals.Gateway.Models;

public class RiskAssessmentResult
{
    public string PatientId { get; set; } = string.Empty;
    public string BedNumber { get; set; } = string.Empty;
    public int MewsScore { get; set; }
    public string TriageLevel { get; set; } = string.Empty;
    public string Recommendation { get; set; } = string.Empty;
}