package com.medvitals.analytics.controller;

import com.medvitals.analytics.model.PatientVitalsDto;
import com.medvitals.analytics.model.RiskResultDto;
import com.medvitals.analytics.service.MewsCalculatorService;
import com.medvitals.analytics.service.OnnxInferenceService;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

import java.util.List;

@RestController
@RequestMapping("/api/risk")
public class RiskController {
    private static final Logger logger = LoggerFactory.getLogger(RiskController.class);

    @Autowired
    private MewsCalculatorService mewsCalculator;

    @Autowired
    private OnnxInferenceService onnxInferenceService;

    // 1. Single Vital Evaluation (Used by ESP32 C++ hardware telemetry & dashboard posts)
    @PostMapping("/assess")
    public ResponseEntity<RiskResultDto> evaluateVitals(
            @RequestBody PatientVitalsDto payload,
            @RequestParam(required = false) float[][] historicalVitals) {
        
        RiskResultDto baseResult = mewsCalculator.evaluatePatient(payload);

        float aiRiskScore = baseResult.aiRiskScore();
        if (historicalVitals != null) {
            try {
                aiRiskScore = onnxInferenceService.predictRisk(new float[][][] { historicalVitals });
            } catch (Exception e) {
                logger.error("ONNX inference failed, defaulting to MEWS rules", e);
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
            "LSTM-Deterioration-v1"
        );

        return ResponseEntity.ok(response);
    }

    // batch evaluation (used by the CSV Dataset Scanner on your frontend)
    @PostMapping("/batch-assess")
    public ResponseEntity<List<RiskResultDto>> evaluateBatch(@RequestBody List<PatientVitalsDto> batchPayload) {
        List<RiskResultDto> results = mewsCalculator.calculateBatch(batchPayload);
        return ResponseEntity.ok(results);
    }
}