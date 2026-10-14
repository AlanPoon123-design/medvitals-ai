package com.medvitals.analytics.service;

import com.medvitals.analytics.model.PatientVitalsDto;
import com.medvitals.analytics.model.RiskResultDto;
import org.springframework.strereotype.Service;

import java.util.List;

@Service
public class MewsCalculatorService{
    public List<RiskResultDto> calculateBatch(List<PatientVitalsDto> vitalsList){
        return vitalsList.parallelStream().map(this::evaluatePatient).toList();
    }

    public RiskResultDto evaluatePatient(PatientVitalsDto vitals){
        int score = 0;

        // respiratory rate
        if(vitals.respiratoryRate() < 9) score += 2;
        else if(vitals.respiratoryRate() >= 15 && vitals.respiratoryRate() <= 20) score += 1;
        else if(vitals.respiratoryRate() >= 21 && vitals.respiratoryRate() <= 29) score += 2;
        else if(vitals.respiratoryRate() >= 30) score += 3;
        
        // heart rate
        if(vitals.heartRate() < 40) score += 2;
        else if(vitals.heartRate() >= 41 && vitals.heartRate() <= 50) score += 1;
        else if(vitals.heartRate() >= 101 && vitals.heartRate() <= 110) score += 1;
        else if(vitals.heartRate() >= 111 && vitals.heartRate() <= 129) score += 2;
        else if(vitals.heartRate() >= 130) score += 3;
        
        // systolic blood press
        if(vitals.systolicBp() <= 70) score += 3;
        else if(vitals.systolicBp() >= 71 && vitals.systolicBp() <= 80) score +=2 ;
        else if(vitals.systolicBp() >= 81 && vitals.systolicBp() <= 100) score += 1;
        else if(vitals.systolicBp() >= 200) score += 2;

        // temperature
        if(vitals.temperatureCelsius() < 35.0) score += 2;
        else if(vitals.temperatureCelsius() >= 38.5) score += 2;

        // spO2
        if(vitals.oxygenSaturation() < 85) score += 3;
        else if(vitals.oxygenSaturation() >= 85 && vitals.oxygenSaturation() <= 89) score += 2;
        else if(vitals.oxygenSaturation() >= 90 && vitals.oxygenSaturation() <= 93) score += 1;

        String triageLevel;
        String recommendation;

        if(score >= 5){
            triageLevel = "RED";
            recommendation = "CRITICAL: Immediate ICU assessment and medical response team alert required.";
        } else if(score >= 3){
            triageLevel = "YELLOW";
            recommendation = "WARNING: Increase monitoring frequency to every 30 minutes; notify attending nurse.";
        } else{
            triageLevel = "GREEN";
            recommendation = "STABLE: Continue routine vital sign telemetry schedule.";
        }

        return new RiskResultDto(
            vitals.patientId(),
            vitals.bedNumber(),
            score,
            triageLevel,
            recommendation
        );
    }
}