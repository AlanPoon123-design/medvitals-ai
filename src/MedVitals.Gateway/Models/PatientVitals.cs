using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MedVitals.Gateway.Models;

public class PatientVitals : IValidatableObject
{
    [Required, StringLength(64, MinimumLength = 1)]
    public string PatientId { get; set; } = string.Empty;

    [StringLength(32)]
    public string BedNumber { get; set; } = string.Empty;

    [Range(20, 300)]
    public int? HeartRate { get; set; }

    [Range(1, 100)]
    public int? RespiratoryRate { get; set; }

    [Range(30, 350)]
    public int? SystolicBp { get; set; }

    [Range(25.0, 45.0)]
    public double? TemperatureCelsius { get; set; }

    [Range(50, 100)]
    public int? OxygenSaturation { get; set; }

    [RegularExpression("^[AVPUavpu]$", ErrorMessage = "Avpu must be one of A, V, P, or U.")]
    public string? Avpu { get; set; }

    /// <summary>False when hardware probe has no valid signal (e.g. finger removed).</summary>
    public bool? SignalValid { get; set; }

    /// <summary>
    /// Optional LSTM input, oldest first; each step is
    /// [heartRate, systolicBp, respiratoryRate, temperatureCelsius, oxygenSaturation].
    /// Forwarded as-is to the analytics engine (omitted from JSON when null).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<float[]>? HistoricalVitals { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var hasAnyVital = HeartRate.HasValue || RespiratoryRate.HasValue || SystolicBp.HasValue
            || TemperatureCelsius.HasValue || OxygenSaturation.HasValue || !string.IsNullOrWhiteSpace(Avpu);

        if (SignalValid != false && !hasAnyVital)
        {
            yield return new ValidationResult(
                "At least one vital sign must be provided unless signalValid is false.",
                new[] { nameof(HeartRate), nameof(RespiratoryRate), nameof(SystolicBp), nameof(TemperatureCelsius), nameof(OxygenSaturation), nameof(Avpu) });
        }

        if (HistoricalVitals is not null)
        {
            if (HistoricalVitals.Count == 0 || HistoricalVitals.Count > MaxHistorySteps)
            {
                yield return new ValidationResult(
                    $"historicalVitals must contain between 1 and {MaxHistorySteps} time steps.",
                    new[] { nameof(HistoricalVitals) });
                yield break;
            }

            for (var i = 0; i < HistoricalVitals.Count; i++)
            {
                var row = HistoricalVitals[i];
                if (row is null || row.Length != HistoryFeatureCount)
                {
                    yield return new ValidationResult(
                        $"historicalVitals[{i}] must contain exactly {HistoryFeatureCount} values.",
                        new[] { nameof(HistoricalVitals) });
                    yield break;
                }
                if (row.Any(v => !float.IsFinite(v)))
                {
                    yield return new ValidationResult(
                        $"historicalVitals[{i}] must contain only finite numbers.",
                        new[] { nameof(HistoricalVitals) });
                    yield break;
                }
            }
        }
    }

    public const int HistoryFeatureCount = 5;
    public const int MaxHistorySteps = 512;
}
