package com.medvitals.analytics.model;

public record RiskResultDto(
    String patientId,
    String bedNumber,
    int mewsScore,
    String triageLevel,
    String recommendation,
    float aiRiskScore,
    String modelVersion
) {}