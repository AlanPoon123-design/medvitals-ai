package com.medvitals.analytics.model;

public record RiskResultDto(
    String patientId,
    String bedNumber,
    int mewsScore,
    String triagLevel,
    String recommendation
) {}