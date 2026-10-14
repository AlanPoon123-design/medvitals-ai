namespace MedVitals.Gateway.Models;

public class PatientVitals
{
    public string PatientId { get; set; } = string.Empty;
    public string BedNumber { get; set; } = string.Empty;
    public int HeartRate { get; set; }
    public int RespiratoryRate { get; set; }
    public int SystolicBp { get; set; }
    public double TemperatureCelsius { get; set; }
    public int OxygenSaturation { get; set; }
}