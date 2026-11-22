package com.medvitals.analytics.service;

import com.medvitals.analytics.model.PatientVitalsDto;
import com.medvitals.analytics.model.RiskResultDto;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.CsvSource;

import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

class MewsCalculatorServiceTest {

    private final MewsCalculatorService service = new MewsCalculatorService();

    private static PatientVitalsDto vitals(int hr, int rr, int sbp, double temp, int spo2) {
        return new PatientVitalsDto("P-1", "B-1", hr, rr, sbp, temp, spo2);
    }

    @Test
    void normalVitalsAreGreen() {
        // RR 12 (0), HR 75 (0), SBP 120 (0), Temp 37.0 (0), SpO2 98 (0)
        RiskResultDto result = service.evaluatePatient(vitals(75, 12, 120, 37.0, 98));

        assertEquals(0, result.mewsScore());
        assertEquals("GREEN", result.triageLevel());
        assertEquals("P-1", result.patientId());
        assertEquals("B-1", result.bedNumber());
        assertEquals(MewsCalculatorService.MODEL_VERSION, result.modelVersion());
        assertEquals(0.0f, result.aiRiskScore());
    }

    @Test
    void heartRateBoundary40ScoresTwo() {
        RiskResultDto result = service.evaluatePatient(vitals(40, 12, 120, 37.0, 98));
        assertEquals(2, result.mewsScore());
    }

    @Test
    void avpuScoringAddsToMewsScore() {
        var voice = new PatientVitalsDto("P-1", "B-1", 75, 12, 120, 37.0, 98, "V", true, null);
        var pain = new PatientVitalsDto("P-1", "B-1", 75, 12, 120, 37.0, 98, "P", true, null);
        var unresponsive = new PatientVitalsDto("P-1", "B-1", 75, 12, 120, 37.0, 98, "U", true, null);

        assertEquals(1, service.evaluatePatient(voice).mewsScore());
        assertEquals(2, service.evaluatePatient(pain).mewsScore());
        assertEquals(3, service.evaluatePatient(unresponsive).mewsScore());
        assertEquals("YELLOW", service.evaluatePatient(unresponsive).triageLevel());
    }

    @Test
    void signalLostReturnsNoSignalWithoutFabricatingNormal() {
        var noSignal = new PatientVitalsDto("P-1", "B-1", null, null, null, null, null, null, false, null);
        RiskResultDto result = service.evaluatePatient(noSignal);

        assertEquals("NO_SIGNAL", result.triageLevel());
        assertEquals(0, result.mewsScore());
    }

    @Test
    void partialVitalsDoNotScoreNullFieldsAsCritical() {
        // Hardware sends only HR=72 and SpO2=98; unmeasured fields are null (not 0)
        var partial = new PatientVitalsDto("P-1", "B-1", 72, null, null, null, 98, null, true, null);
        RiskResultDto result = service.evaluatePatient(partial);

        assertEquals(0, result.mewsScore());
        assertEquals("GREEN", result.triageLevel());
    }

    @Test
    void moderateDerangementIsYellow() {
        // RR 22 (2), HR 105 (1), others normal -> 3
        RiskResultDto result = service.evaluatePatient(vitals(105, 22, 120, 37.0, 98));

        assertEquals(3, result.mewsScore());
        assertEquals("YELLOW", result.triageLevel());
    }

    @Test
    void severeDerangementIsRed() {
        // RR 32 (3), HR 135 (3), SBP 65 (3), Temp 39.0 (2), SpO2 80 (3) -> 14
        RiskResultDto result = service.evaluatePatient(vitals(135, 32, 65, 39.0, 80));

        assertEquals(14, result.mewsScore());
        assertEquals("RED", result.triageLevel());
    }

    @ParameterizedTest(name = "SpO2 {0} -> {1}")
    @CsvSource({"84,3", "85,2", "89,2", "90,1", "93,1", "94,0"})
    void oxygenSaturationBands(int spo2, int expected) {
        assertEquals(expected, service.evaluatePatient(vitals(75, 12, 120, 37.0, spo2)).mewsScore());
    }

    @ParameterizedTest(name = "SBP {0} -> {1}")
    @CsvSource({"70,3", "71,2", "80,2", "81,1", "100,1", "101,0", "199,0", "200,2"})
    void systolicBloodPressureBands(int sbp, int expected) {
        assertEquals(expected, service.evaluatePatient(vitals(75, 12, sbp, 37.0, 98)).mewsScore());
    }

    @Test
    void batchPreservesOrderAndSize() {
        List<PatientVitalsDto> batch = List.of(
            new PatientVitalsDto("A", "1", 75, 12, 120, 37.0, 98),
            new PatientVitalsDto("B", "2", 135, 32, 65, 39.0, 80)
        );

        List<RiskResultDto> results = service.calculateBatch(batch);

        assertEquals(2, results.size());
        assertEquals("A", results.get(0).patientId());
        assertEquals("GREEN", results.get(0).triageLevel());
        assertEquals("B", results.get(1).patientId());
        assertEquals("RED", results.get(1).triageLevel());
    }
}
