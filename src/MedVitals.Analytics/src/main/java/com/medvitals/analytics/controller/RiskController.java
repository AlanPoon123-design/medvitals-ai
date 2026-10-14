package com.medvitals.analytics.controller;

import com.medvitals.analytics.model.PatientVitalsDto;
import com.medvitals.analytics.model.RiskResultDto;
import com.medvitals.analytics.service.MewsCalculatorService;
import org.springframework.web.bind.annotation.*;

import java.util.List;

@RestController
@RequestMapping("/app/risk")

public class RiskController{
    private final MewsCalculatorService calculatorService;

    public RiskController(MewsCalaculatorService calculatorSerivce){
        this.calculatorService = calculatorService;
    }

    @PostMapping("/assess")
    public List<RiskResultDto> assessRiskBatch(@RequestBody List<PatietnVitalsDto> vitalsBatch){
        return calculatorService.calcualteBatch(vitalsBatch);
    }
}