package com.medvitals.analytics.controller;

import com.medvitals.analytics.model.PatientVitalsDto;
import com.medvitals.analytics.model.RiskResultDto;
import com.medvitals.analytics.service.MewsCalculatorService;
import com.medvitals.analytics.service.OnnxInferenceService;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.autoconfigure.web.servlet.WebMvcTest;
import org.springframework.boot.test.mock.mockito.MockBean;
import org.springframework.http.MediaType;
import org.springframework.test.web.servlet.MockMvc;

import java.util.List;

import static org.mockito.ArgumentMatchers.any;
import static org.mockito.ArgumentMatchers.anyList;
import static org.mockito.Mockito.never;
import static org.mockito.Mockito.verify;
import static org.mockito.Mockito.when;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

@WebMvcTest(RiskController.class)
class RiskControllerTest {

    private static final String VALID = """
        {"patientId":"P-1","bedNumber":"B-1","heartRate":75,"respiratoryRate":12,
         "systolicBp":120,"temperatureCelsius":37.0,"oxygenSaturation":98}
        """;

    @Autowired
    private MockMvc mvc;

    @MockBean
    private MewsCalculatorService mewsCalculator;

    @MockBean
    private OnnxInferenceService onnxInferenceService;

    private static RiskResultDto green() {
        return new RiskResultDto("P-1", "B-1", 0, "GREEN", "STABLE", 0.0f, "MEWS-NEWS-Hybrid-v1");
    }

    @Test
    void assessMissingAllVitalsWhenSignalValidReturns400() throws Exception {
        String body = "{\"patientId\":\"P-1\",\"bedNumber\":\"B-1\"}";

        mvc.perform(post("/api/risk/assess").contentType(MediaType.APPLICATION_JSON).content(body))
            .andExpect(status().isBadRequest());
        verify(mewsCalculator, never()).evaluatePatient(any());
    }

    @Test
    void assessValidPayloadReturns200() throws Exception {
        when(mewsCalculator.evaluatePatient(any(PatientVitalsDto.class))).thenReturn(green());

        mvc.perform(post("/api/risk/assess").contentType(MediaType.APPLICATION_JSON).content(VALID))
            .andExpect(status().isOk())
            .andExpect(jsonPath("$.triageLevel").value("GREEN"));
    }

    @Test
    void assessMissingPatientIdReturns400() throws Exception {
        String body = VALID.replace("\"patientId\":\"P-1\",", "");

        mvc.perform(post("/api/risk/assess").contentType(MediaType.APPLICATION_JSON).content(body))
            .andExpect(status().isBadRequest());
        verify(mewsCalculator, never()).evaluatePatient(any());
    }

    @Test
    void assessOutOfRangeSpo2Returns400() throws Exception {
        String body = VALID.replace("\"oxygenSaturation\":98", "\"oxygenSaturation\":150");

        mvc.perform(post("/api/risk/assess").contentType(MediaType.APPLICATION_JSON).content(body))
            .andExpect(status().isBadRequest());
    }

    @Test
    void assessWithHistoricalVitalsRunsOnnxAndEscalates() throws Exception {
        when(mewsCalculator.evaluatePatient(any(PatientVitalsDto.class))).thenReturn(green());
        when(onnxInferenceService.predictRisk(any(float[][][].class))).thenReturn(0.88f);

        String body = """
            {"patientId":"P-1","bedNumber":"B-1","heartRate":75,"respiratoryRate":12,
             "systolicBp":120,"temperatureCelsius":37.0,"oxygenSaturation":98,
             "historicalVitals":[[72,120,12,36.8,98],[75,120,12,37.0,98]]}
            """;

        mvc.perform(post("/api/risk/assess").contentType(MediaType.APPLICATION_JSON).content(body))
            .andExpect(status().isOk())
            .andExpect(jsonPath("$.aiRiskScore").value(0.88))
            .andExpect(jsonPath("$.triageLevel").value("RED"))
            .andExpect(jsonPath("$.modelVersion").value("LSTM-Deterioration-v1"));
    }

    @Test
    void assessWithWrongHistoryFeatureCountReturns400() throws Exception {
        String body = """
            {"patientId":"P-1","bedNumber":"B-1","heartRate":75,"respiratoryRate":12,
             "systolicBp":120,"temperatureCelsius":37.0,"oxygenSaturation":98,
             "historicalVitals":[[72,120,12]]}
            """;

        mvc.perform(post("/api/risk/assess").contentType(MediaType.APPLICATION_JSON).content(body))
            .andExpect(status().isBadRequest());
        verify(onnxInferenceService, never()).predictRisk(any());
    }

    @Test
    void batchValidPayloadReturns200() throws Exception {
        when(mewsCalculator.calculateBatch(anyList())).thenReturn(List.of(green(), green()));

        mvc.perform(post("/api/risk/batch-assess").contentType(MediaType.APPLICATION_JSON)
                .content("[" + VALID + "," + VALID + "]"))
            .andExpect(status().isOk())
            .andExpect(jsonPath("$.length()").value(2));
    }

    @Test
    void batchEmptyReturns400() throws Exception {
        mvc.perform(post("/api/risk/batch-assess").contentType(MediaType.APPLICATION_JSON).content("[]"))
            .andExpect(status().isBadRequest());
        verify(mewsCalculator, never()).calculateBatch(anyList());
    }

    @Test
    void batchWithInvalidElementReturns400() throws Exception {
        String bad = VALID.replace("\"heartRate\":75", "\"heartRate\":-5");

        mvc.perform(post("/api/risk/batch-assess").contentType(MediaType.APPLICATION_JSON)
                .content("[" + VALID + "," + bad + "]"))
            .andExpect(status().isBadRequest());
        verify(mewsCalculator, never()).calculateBatch(anyList());
    }
}
