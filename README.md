# MedVitals AI

> **Real-Time Clinical Observability & Silent Patient Deterioration Prevention Engine**  
> *Developed for the 2026 UCR BCOE Bear Tank Competition.*

[![ASP.NET Core 8.0](https://img.shields.io/badge/API%20Gateway-ASP.NET%20Core%208.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Java 21](https://img.shields.io/badge/Analytics%20Engine-Java%2021%20Spring%20Boot-007396?logo=openjdk)](https://spring.io/)
[![ONNX Runtime](https://img.shields.io/badge/Inference-ONNX%20Runtime-00599C?logo=onnx)](https://onnxruntime.ai/)
[![PyTorch](https://img.shields.io/badge/Model%20Training-PyTorch-EE4C2C?logo=pytorch&logoColor=white)](https://pytorch.org/)
[![Docker](https://img.shields.io/badge/Containerization-Docker%20%26%20Compose-2496ED?logo=docker)](https://www.docker.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

---

## Overview

Hospitalized patients often show measurable warning signs in their vital signs hours before sepsis, respiratory failure, or cardiac arrest. However, vitals are usually scored manually at long intervals, meaning critical physiological trends are caught too late.

**MedVitals AI** is a lightweight, hardware-agnostic clinical decision support platform that:
* **Automates MEWS:** Instantly calculates the Modified Early Warning Score (MEWS) on every incoming vital payload.
* **Predictive AI Modeling:** Evaluates continuous trends across time-series physiological data rather than relying on isolated snapshots.
* **Actionable Risk Delivery:** Immediately returns risk stratifications and escalation recommendations in a unified JSON response.

Unlike proprietary deterioration tools tied to specific vendor hardware or heavy, slow EHR monoliths, MedVitals AI accepts vitals from any telemetry source via standard REST endpoints and deploys cleanly to local edge hardware using a single command: `docker-compose up`.

---

## Tech Stack & Architecture

| Layer | Technology | Role |
| :--- | :--- | :--- |
| **API Gateway** | ASP.NET Core 8 (C#) | Hardware-agnostic ingestion, validation, and high-throughput routing of vital-sign payloads |
| **Analytics Engine** | Java 21 + Spring Boot | Real-time MEWS calculation and clinical rule evaluation |
| **Model Inference** | ONNX Runtime (Java API) | Embedded, in-process execution of trained neural networks directly within the Java engine |
| **Model Training** | PyTorch | Offline training of time-series models on vital trends; exported to ONNX |
| **Deployment** | Docker & Docker Compose | Containerized microservice isolation for low-latency local or edge deployment |

### Architectural Rationale
* **Separation of Concerns:** The C# Gateway ingests data from diverse sensor inputs and scales independently from the core analytical engine.
* **Zero-Latency Inference:** Training remains in Python for ML tooling flexibility, but embedding the ONNX Runtime directly inside Java eliminates separate model server overhead and avoids extra network latency during inference.

---

## Request & Scoring Workflow

### Request Flow
1. **Payload Ingestion:** A client or IoT telemetry node posts vital payloads to the ASP.NET Core Gateway.
2. **Validation:** The Gateway validates units, bounds, and ranges before forwarding payloads to the Analytics Engine.
3. **Execution:** The Java engine computes real-time MEWS metrics and runs in-memory ONNX model inference.
4. **Response Delivery:** Both scoring results are compiled into a structured clinical risk report returned instantly as JSON.

### MEWS Scoring Standard
Incoming vitals receive sub-scores ranging from `0` to `3` based on deviations from baseline physiological ranges. Higher composite totals indicate elevated clinical risk. 

> **Example:** A payload scoring `7` indicates high risk and triggers immediate rapid response protocol recommendations.

---

## Getting Started

### Prerequisites
* [Docker Desktop](https://www.docker.com/products/docker-desktop/) (running container daemon)
* [Git](https://git-scm.com/)

### Local Setup & Launch
```bash
# Clone the repository
git clone [https://github.com/AlanPoon123-design/medvitals-ai.git](https://github.com/AlanPoon123-design/medvitals-ai.git)

# Navigate into the project directory
cd medvitals-ai

# Build and start containerized microservices
docker-compose up

# Local Setup & Launch
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

# Sample Response
{
  "patientId": "demo-001",
  "mews": {
    "total": 7,
    "breakdown": {
      "heartRate": 2,
      "systolicBp": 1,
      "respiratoryRate": 2,
      "temperature": 2,
      "avpu": 0
    },
    "riskLevel": "HIGH"
  },
  "aiRisk": {
    "score": 0.00,
    "model": "<model-name>"
  },
  "escalation": "Notify rapid response team per hospital protocol"
}
