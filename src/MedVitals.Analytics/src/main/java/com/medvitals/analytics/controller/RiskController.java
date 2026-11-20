package com.medvitals.analytics.controller;

import com.medvitals.analytics.model.PatientVitalsDto;
import com.medvitals.analytics.model.RiskResultDto;
import com.medvitals.analytics.service.MewsCalculatorService;
import com.medvitals.analytics.service.OnnxInferenceService;

import jakarta.validation.Valid;
import jakarta.validation.constraints.NotEmpty;
import jakarta.validation.constraints.Size;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;
import org.springframework.web.server.ResponseStatusException;

import java.util.List;

// No class-level @Validated: Spring 6.1 built-in method validation handles the @Valid / @NotEmpty
// parameter constraints and maps failures to 400 (MethodArgumentNotValidException / HandlerMethodValidationException).
@RestController
@RequestMapping("/api/risk")
public class RiskController {
    private static final Logger logger = LoggerFactory.getLogger(RiskController.class);

    private final MewsCalculatorService mewsCalculator;
    private final OnnxInferenceService onnxInferenceService;

    public RiskController(MewsCalculatorService mewsCalculator, OnnxInferenceService onnxInferenceService) {
        this.mewsCalculator = mewsCalculator;
        this.onnxInferenceService = onnxInferenceService;
    }

    // 1. Single Vital Evaluation (Used by ESP32 C++ hardware telemetry & dashboard posts)
    // optional "historicalVitals" in the JSON body enables the LSTM branch
    @PostMapping("/assess")
    public ResponseEntity<RiskResultDto> evaluateVitals(@Valid @RequestBody PatientVitalsDto payload) {

        if (!payload.isSignalLost() && !payload.hasAnyVital()) {
            throw new ResponseStatusException(HttpStatus.BAD_REQUEST,
                "At least one vital sign must be provided when signalValid is not false");
        }

        String historyError = payload.historyValidationError();
        if (historyError != null) {
            throw new ResponseStatusException(HttpStatus.BAD_REQUEST, historyError);
        }
        
        RiskResultDto baseResult = mewsCalculator.evaluatePatient(payload);

        float aiRiskScore = baseResult.aiRiskScore();
        String modelVersion = baseResult.modelVersion();
        if (!payload.isSignalLost() && payload.hasHistory()) {
            try {
                aiRiskScore = onnxInferenceService.predictRisk(payload.historyAsBatchTensor());
                modelVersion = "LSTM-Deterioration-v1";
            } catch (Exception e) {
                logger.error("ONNX inference failed, defaulting to MEWS/NEWS rules", e);
            }
        }

        // if AI score overrides threshold, adjust escalation recommendation
        String triageLevel = baseResult.triageLevel();
        String recommendation = baseResult.recommendation();
        if (aiRiskScore > 0.75f) {
            triageLevel = "RED";
            recommendation = "HIGH RISK (AI LSTM): Immediate Rapid Response Team (RRT) Triggered";
        }

        RiskResultDto response = new RiskResultDto(
            baseResult.patientId(),
            baseResult.bedNumber(),
            baseResult.mewsScore(),
            triageLevel,
            recommendation,
            aiRiskScore,
            modelVersion
        );

        return ResponseEntity.ok(response);
    }

    // batch evaluation (used by the CSV Dataset Scanner on the frontend)
    @PostMapping("/batch-assess")
    public ResponseEntity<List<RiskResultDto>> evaluateBatch(
            @RequestBody @NotEmpty @Size(max = 10_000) List<@Valid PatientVitalsDto> batchPayload) {
        List<RiskResultDto> results = mewsCalculator.calculateBatch(batchPayload);
        return ResponseEntity.ok(results);
    }
}
