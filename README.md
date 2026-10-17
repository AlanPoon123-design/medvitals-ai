MedVitals AI
Developed for the 2026 UCR BCOE Bear Tank Competition.
ASP.NET Core 8.0 Java 21 Docker License: MIT
⚠️ Research prototype — not for clinical use. MedVitals AI has not been clinically validated or cleared by any regulatory body. Do not use it to make decisions about real patients.

Summary
Hospitalized patients often show measurable warning signs in their vital signs hours before sepsis, respiratory failure, or cardiac arrest. But vitals are usually scored by hand at intervals, so these trends are often caught too late.
MedVitals AI is a lightweight, hardware-agnostic clinical decision support platform that:
Automates the Modified Early Warning Score (MEWS) on every incoming set of vitals.
Runs a predictive time-series model that looks at trends across a patient's recent vitals, not just a single snapshot.
Returns a risk level and escalation recommendation in a single JSON response.
Unlike deterioration tools tied to a specific monitor vendor or embedded in a large EHR system, MedVitals AI accepts vitals from any source through a standard REST API and runs entirely on local hardware. It deploys with a single docker-compose up.

Tech Stack
Layer
Technology
Role
API Gateway
ASP.NET Core 8 (C#)
Hardware-agnostic ingestion, validation, and routing of vital-sign payloads
Analytics Engine
Java 21 + Spring Boot
Real-time MEWS calculation and clinical rule evaluation
Model Inference
ONNX Runtime (Java API)
In-process execution of the trained model inside the Java engine
Model Training
PyTorch
Offline training of the time-series model; exported to ONNX
Deployment
Docker + Docker Compose
Isolated services, single-command local or edge deployment

Why this split? The gateway handles input from many device types and can scale independently from the scoring engine. Training stays in Python, where the ML tooling lives. Running the model through ONNX inside Java avoids a separate model server and an extra network hop at inference time.

Software Architecture


Request flow
A client sends a vitals payload to the API Gateway.
The gateway validates units and ranges, then forwards the payload to the Analytics Engine.
The engine computes MEWS and runs the ONNX model in memory.
Both results are combined into a risk report and returned as JSON.
MEWS scoring reference
The Analytics Engine uses the table above to score every incoming set of vital, which gets a score of 0 to 3 based on how far its gotten from the normal range. The higher the total, the more risk there is of deterioration. This gives the cinnicians a familiar baseline. For example, the sample payload under Getting Started scores 7, which counts as high risk. 
Getting Started
Prerequisites
Docker Desktop
Run
git clone https://github.com/AlanPoon123-design/medvitals-ai.git
cd medvitals-ai
docker-compose up

Send a test payload
curl -X POST http://localhost:<GATEWAY_PORT>/<VITALS_ENDPOINT> \
  -H "Content-Type: application/json" \
  -d '{
        "patientId": "demo-001",
        "heartRate": 125,
        "systolicBp": 82,
        "respiratoryRate": 24,
        "temperatureC": 38.8,
        "avpu": "ALERT"
      }'

Example response
{
  "patientId": "demo-001",
  "mews": {
    "total": 7,
    "breakdown": { "heartRate": 2, "systolicBp": 1, "respiratoryRate": 2, "temperature": 2, "avpu": 0 },
    "riskLevel": "HIGH"
  },
  "aiRisk": { "score": 0.00, "model": "<model-name>" },
  "escalation": "Notify rapid response team per hospital protocol"
}



Milestones to Achieve:
Accuracy results in the MEWS alone. 
It becomes readable so clinicians don;t need to read JSON. 
Make sure that users data is safe and secure. 
False-alert rate is at a low level  
