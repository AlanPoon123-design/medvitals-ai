package com.medvitals.analytics.model;

public record PatientVitalsDto(
    String patientId,
    String bedNumber,
    int heartRate, // in bpm
    int respiratoryRate, //breaths/min
    int systolicBp, // in mmHg
    double temperatureCelsius,
    int oxygenSaturation // in Sp02 %
) {}