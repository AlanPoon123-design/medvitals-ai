package com.medvitals.analytics.model;

import jakarta.validation.constraints.DecimalMax;
import jakarta.validation.constraints.DecimalMin;
import jakarta.validation.constraints.Max;
import jakarta.validation.constraints.Min;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotNull;
import jakarta.validation.constraints.Pattern;
import jakarta.validation.constraints.Size;

import java.util.List;

public record PatientVitalsDto(
    @NotBlank @Size(max = 64) String patientId,
    @Size(max = 32) String bedNumber,
    @Min(20) @Max(300) Integer heartRate,              // in bpm (null if not measured)
    @Min(1) @Max(100) Integer respiratoryRate,         // breaths/min (null if not measured)
    @Min(30) @Max(350) Integer systolicBp,             // in mmHg (null if not measured)
    @DecimalMin("25.0") @DecimalMax("45.0") Double temperatureCelsius, // null if not measured
    @Min(50) @Max(100) Integer oxygenSaturation,       // SpO2 % (null if not measured)
    @Pattern(regexp = "^[AVPUavpu]$", message = "avpu must be one of A, V, P, U")
    String avpu,                                       // Alert, Voice, Pain, Unresponsive (optional)
    Boolean signalValid,                               // false when hardware probe has no signal

    /*
     * Optional time series for the LSTM model, oldest first. Each time step holds
     * FEATURE_COUNT values in this order:
     *   [heartRate, systolicBp, respiratoryRate, temperatureCelsius, oxygenSaturation]
     * When absent, only the rule-based MEWS/NEWS hybrid score is computed.
     */
    // NOTE: per-row length (FEATURE_COUNT) and finiteness are checked by historyValidationError():
    // a type annotation written as "@Size float[]" binds to the element type float, not the array.
    @Size(min = 1, max = MAX_HISTORY_STEPS)
    List<float[]> historicalVitals
) {
    public static final int FEATURE_COUNT = 5;
    public static final int MAX_HISTORY_STEPS = 512;

    /** Convenience constructor for full current-reading payloads (no AVPU/history). */
    public PatientVitalsDto(String patientId, String bedNumber, Integer heartRate, Integer respiratoryRate,
                            Integer systolicBp, Double temperatureCelsius, Integer oxygenSaturation) {
        this(patientId, bedNumber, heartRate, respiratoryRate, systolicBp, temperatureCelsius,
             oxygenSaturation, null, Boolean.TRUE, null);
    }

    public boolean isSignalLost() {
        return Boolean.FALSE.equals(signalValid);
    }

    public boolean hasAnyVital() {
        return heartRate != null || respiratoryRate != null || systolicBp != null
            || temperatureCelsius != null || oxygenSaturation != null || avpu != null;
    }

    public boolean hasHistory() {
        return historicalVitals != null && !historicalVitals.isEmpty();
    }

    /** @return null if the history is absent or well-formed, otherwise a human-readable error. */
    public String historyValidationError() {
        if (!hasHistory()) return null;
        for (int i = 0; i < historicalVitals.size(); i++) {
            float[] row = historicalVitals.get(i);
            if (row == null || row.length != FEATURE_COUNT) {
                return "historicalVitals[" + i + "] must contain exactly " + FEATURE_COUNT + " values";
            }
            for (float v : row) {
                if (!Float.isFinite(v)) {
                    return "historicalVitals[" + i + "] must contain only finite numbers";
                }
            }
        }
        return null;
    }

    /** History as the [1][steps][features] tensor layout expected by OnnxInferenceService. */
    public float[][][] historyAsBatchTensor() {
        return new float[][][] { historicalVitals.toArray(new float[0][]) };
    }
}
