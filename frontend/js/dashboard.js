document.addEventListener("DOMContentLoaded", () => {
    const GATEWAY_URL = "http://localhost:5000/api/vitals";
    let hardwarePollingInterval = null;

    // initialize Chart.js
    const ctx = document.getElementById('riskChart').getContext('2d');
    const riskChart = new Chart(ctx,{
        type: 'line',
        data: {
            labels: [],
            datasets: [{
                label: 'Deterioration Risk (%)',
                data: [],
                borderColor: '#0284c7',
                backgroundColor: 'rgba(2, 132, 199, 0.05)',
                borderWidth: 2,
                fill: true,
                tension: 0.2
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            scales: {
                y: { min: 0, max: 100, grid: { color: '#f1f5f9' } },
                x: { grid: { display: false } }
            }
        }
    });

    // 1. dataset scanner execution
    document.getElementById('btn-scan').addEventListener('click', ()=> {
        const fileInput = document.getElementById('csv-file');
        const file = fileInput.files[0];

        if(!file){
            alert("Please select a clinical CSV dataset file to scan.");
            return;
        }

        // pause live hardware polling during dataset file scan
        if(hardwarePollingInterval) clearInterval(hardwarePollingInterval);
        document.getElementById('source-tag').innerText = "ACTIVE: DATASET SCAN RUNNING";

        const reader = new FileReader();
        reader.onload = (event) => {
            const lines = event.target.result.split('\n').slice(1);
            const dataset = lines.map(line => {
                const parts = line.split(',');
                if(parts.length < 5) return null;
                return {
                    patientId: "PATIENT-FILE-SCAN",
                    heartRate: parseInt(parts[0]),
                    systolicBP: parseInt(parts[1]),
                    respiratoryRate: parseInt(parts[2]),
                    temperature: parseFloat(parts[3]),
                    spo2: parseInt(parts[4])
                };
            }).filter(item => item !== null);
            processScannedBatch(dataset);
        };
        reader.readAsText(file);
    });

    async function processScannedBatch(dataset){
        for(const record of dataset){
            await sendTelemetryPayload(record);
            await new Promise(resolve => setTimeout(resolve, 1500));
        }
    }

    // continuous hardware polling / receiver
    function startHardwareTelemetryStream(){
        document.getElementById('source-tag').innerText = "ACTIVE: HARDWARE TELEMETRY STREAM";
        hardwarePollingInterval = setInterval(async () => {
            try{
                const res = await fetch(`${GATEWAY_URL}/PATIENT-001`);
                const data = await res.json();
                updateDashboardUI(data);
            } catch(err){
                // listening for next incoming hardware transmission
            }
        }, 2000);
    }

    // helper function for sending telemetry to backend engine
    async function sendTelemetryPayload(payload){
        try{
            const res = await fetch(GATEWAY_URL, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(payload)
            });

            if(res.ok){
                const data = await res.json();
                updateDashboardUI(data);
            } 
        } catch(err){
            console.error("Backend Gateway unreachable", err);
        }
    }

    // update UI elements
    function updateDashboardUI(data){
        document.getElementById('mews-val').innerText = data.mewsResult?.score ?? data.mewsScore ?? 0;

        const rawAiScore = data.aiRiskResult?.score ?? data.aiRiskScore ?? 0;
        const aiPct = Math.round(rawAiScore * 100);
        document.getElementById('ai-val').innerText = `${aiPct}%`;

        const badge = document.getElementById('escalation-badge');
        badge.innerText = data.escalation || "LOW RISK: Standard ward monitoring";

        if (aiPct > 75 || (data.mewsResult?.score || 0) >= 5) {
            badge.className = "alert-badge badge-high";
        } else if (aiPct > 40 || (data.mewsResult?.score || 0) >= 3) {
            badge.className = "alert-badge badge-medium";
        } else {
            badge.className = "alert-badge badge-low";
        }

        // plot trend
        const timestamp = new Date().toLocaleTimeString();
        if(riskChart.data.labels.length >= 10){
            riskChart.data.labels.shift();
            riskChart.data.datasets[0].data.shift();
        }
        riskChart.data.labels.push(timestamp);
        riskChart.data.datasets[0].data.push(aiPct);
        riskChart.update();
    }

    startHardwareTelemetryStream();
});