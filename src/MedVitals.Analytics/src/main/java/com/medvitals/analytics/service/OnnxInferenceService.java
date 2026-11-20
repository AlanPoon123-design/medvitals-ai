package com.medvitals.analytics.service;

import ai.onnxruntime.*;
import org.springframework.stereotype.Service;
import jakarta.annotation.PostConstruct;
import jakarta.annotation.PreDestroy;

import java.io.IOException;
import java.io.InputStream;
import java.nio.FloatBuffer;
import java.util.Collections;

@Service
public class OnnxInferenceService {
    private static final String MODEL_RESOURCE = "deterioration_model.onnx";

    private OrtEnvironment env;
    private OrtSession session;

    @PostConstruct
    public void init() throws OrtException, IOException {
        // initialize ONNX Runtime environment and session
        this.env = OrtEnvironment.getEnvironment();
        // load the model as bytes: a filesystem path from getResource().getPath() does not work
        // once the resource is packaged inside the Spring Boot fat jar
        try (InputStream in = getClass().getClassLoader().getResourceAsStream(MODEL_RESOURCE)) {
            if (in == null) {
                throw new IllegalStateException("ONNX model resource not found on classpath: " + MODEL_RESOURCE);
            }
            this.session = env.createSession(in.readAllBytes(), new OrtSession.SessionOptions());
        }
    }

    /**
     * Runs inference on time-series vitlas sequence.
     * @param vitalsSequence float array of shape [1][sequence_length][5]
     * @return risk score probability between 0.0 and 1.0
     */
    public float predictRisk(float[][][] vitalsSequence) throws OrtException{
        int sequenceLength = vitalsSequence[0].length;
        int featureCount = vitalsSequence[0][0].length; // 5 vitals

        // flatten sequence array into 1D float buffer for ONNX Tensor
        float[] flatData = new float[sequenceLength * featureCount];
        int idx = 0;
        for(int t = 0; t < sequenceLength; t++){
            for(int f = 0; f < featureCount; f++){
                flatData[idx++] = vitalsSequence[0][t][f];
            }
        }

        long[] shape = new long[]{1, sequenceLength, featureCount};
        OnnxTensor inputTensor = OnnxTensor.createTensor(env, FloatBuffer.wrap(flatData), shape);

        try(OrtSession.Result results = session.run(Collections.singletonMap("vitals_sequence", inputTensor))){
            OnnxValue outputValue = results.get(0);
            float[][] outputArray = (float[][]) outputValue.getValue();
            return outputArray[0][0];
        } finally{
            inputTensor.close();
        }
    }

    @PreDestroy
    public void cleanup() throws OrtException{
        if(session != null) session.close();
        if (env != null) env.close();
    }
}
