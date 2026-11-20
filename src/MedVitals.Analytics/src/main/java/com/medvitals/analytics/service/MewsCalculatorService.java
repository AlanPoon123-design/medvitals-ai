package com.medvitals.analytics.service;

import com.medvitals.analytics.model.PatientVitalsDto;
import com.medvitals.analytics.model.RiskResultDto;
import org.springframework.stereotype.Service;

import java.util.List;
import java.util.Locale;

/**
 * Computes a MEWS/NEWS hybrid early-warning score from whichever vital signs are present.
 * Standard MEWS parameters: respiratory rate, heart rate, systolic BP, temperature, AVPU.
 * NEWS2 addition: oxygen saturation (SpO2).
 */
@Service
public class MewsCalculatorService {

    public static final String MODEL_VERSION = "MEWS-NEWS-Hybrid-v1";

    public List<RiskResultDto> calculateBatch(List<PatientVitalsDto> vitalsList) {
        return vitalsList.parallelStream().map(this::evaluatePatient).toList();
    }

    public RiskResultDto evaluatePatient(PatientVitalsDto vitals) {
        if (vitals.isSignalLost()) {
            return new RiskResultDto(
                vitals.patientId(),
                vitals.bedNumber(),
                0,
                "NO_SIGNAL",
                "NO SIGNAL: Sensor detached or no finger detected — check probe placement.",
                0.0f,
                MODEL_VERSION
            );
        }

        int score = 0;

        // respiratory rate (breaths/min)
        if (vitals.respiratoryRate() != null) {
            int rr = vitals.respiratoryRate();
            if (rr < 9) score += 2;
            else if (rr >= 15 && rr <= 20) score += 1;
            else if (rr >= 21 && rr <= 29) score += 2;
            else if (rr >= 30) score += 3;
        }

        // heart rate (bpm) — <= 40 scores 2 (fixes gap where exactly 40 previously scored 0)
        if (vitals.heartRate() != null) {
            int hr = vitals.heartRate();
            if (hr <= 40) score += 2;
            else if (hr <= 50) score += 1;
            else if (hr >= 101 && hr <= 110) score += 1;
            else if (hr >= 111 && hr <= 129) score += 2;
            else if (hr >= 130) score += 3;
        }

        // systolic blood pressure (mmHg)
        if (vitals.systolicBp() != null) {
            int sbp = vitals.systolicBp();
            if (sbp <= 70) score += 3;
            else if (sbp <= 80) score += 2;
            else if (sbp <= 100) score += 1;
            else if (sbp >= 200) score += 2;
        }

        // temperature (°C)
        if (vitals.temperatureCelsius() != null) {
            double temp = vitals.temperatureCelsius();
            if (temp < 35.0 || temp >= 38.5) score += 2;
        }

        // AVPU consciousness level (standard MEWS)
        if (vitals.avpu() != null && !vitals.avpu().isBlank()) {
            switch (vitals.avpu().trim().toUpperCase(Locale.ROOT)) {
                case "V" -> score += 1;
                case "P" -> score += 2;
                case "U" -> score += 3;
                default -> { /* A = 0 */ }
            }
        }

        // SpO2 % (NEWS2 parameter; part of this MEWS/NEWS hybrid)
        if (vitals.oxygenSaturation() != null) {
            int spo2 = vitals.oxygenSaturation();
            if (spo2 < 85) score += 3;
            else if (spo2 <= 89) score += 2;
            else if (spo2 <= 93) score += 1;
        }

        String triageLevel;
        String recommendation;

        if (score >= 5) {
            triageLevel = "RED";
            recommendation = "CRITICAL: Immediate ICU assessment and medical response team alert required.";
        } else if (score >= 3) {
            triageLevel = "YELLOW";
            recommendation = "WARNING: Increase monitoring frequency to every 30 minutes; notify attending nurse.";
        } else {
            triageLevel = "GREEN";
            recommendation = "STABLE: Continue routine vital sign telemetry schedule.";
        }

        return new RiskResultDto(
            vitals.patientId(),
            vitals.bedNumber(),
            score,
            triageLevel,
            recommendation,
            0.0f,
            MODEL_VERSION
        );
    }
}
