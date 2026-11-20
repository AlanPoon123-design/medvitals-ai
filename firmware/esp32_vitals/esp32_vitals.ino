#include <WiFi.h>
#include <HTTPClient.h>
#include <WiFiClientSecure.h>
#include <Wire.h>
#include "MAX30105.h"
#include "heartRate.h"

// ---------------- Wi-Fi & Gateway Configuration ----------------
const char* ssid = "YOUR_WIFI_SSID";
const char* password = "YOUR_WIFI_PASSWORD";

// Set USE_TLS to 1 and point gatewayUrl at an https:// endpoint (e.g. a TLS-terminating
// reverse proxy in front of the gateway) to encrypt telemetry in transit.
#define USE_TLS 0
#if USE_TLS
const char* gatewayUrl = "https://medvitals.example.local/api/vitals";
// PEM root CA that signed the gateway/proxy certificate
const char* gatewayRootCa = R"PEM(
-----BEGIN CERTIFICATE-----
REPLACE_WITH_ROOT_CA
-----END CERTIFICATE-----
)PEM";
#else
const char* gatewayUrl = "http://192.168.1.100:5000/api/vitals"; // replace with local machine IP
#endif

// must match the gateway's Auth:DeviceApiKey (sent as X-Api-Key)
const char* deviceApiKey = "REPLACE_WITH_DEVICE_API_KEY";

const char* patientId = "PATIENT-001";
const char* bedNumber = "BED-01";

// ---------------- Timing & Sensor Thresholds ----------------
const unsigned long transmitInterval = 2000;      // ms between telemetry posts
const uint16_t httpTimeoutMs = 1500;              // per-request HTTP timeout
const unsigned long wifiReconnectInterval = 5000; // ms between reconnect attempts
const long FINGER_IR_THRESHOLD = 50000;           // minimum IR reading for finger presence

// ---------------- Sensor state (owned by loop() on core 1) ----------------
MAX30105 particleSensor;

const byte RATE_SIZE = 4; // increase for more smoothing
byte rates[RATE_SIZE];
byte rateSpot = 0;
byte rateCount = 0;       // number of valid samples collected (prevents zero-dilution)
long lastBeat = 0;        // time at which the last beat occurred

int beatAvg = 0;
int spo2 = 0;

// AC/DC tracking over the transmit window for empirical SpO2 R-ratio estimation
// (note: clinical accuracy requires calibration against a reference pulse oximeter)
uint32_t irMin = UINT32_MAX, irMax = 0;
uint32_t redMin = UINT32_MAX, redMax = 0;
uint64_t irSum = 0, redSum = 0;
uint32_t sampleCount = 0;

unsigned long lastSampleQueuedTime = 0;

// ---------------- Telemetry hand-off to network task ----------------
struct TelemetrySample {
    bool signalValid;
    int heartRate;
    int spo2;
};

// length-1 queue: loop() overwrites with the latest sample, network task consumes it.
// Keeps the blocking HTTP POST off the sensor loop so beats are not missed.
QueueHandle_t telemetryQueue;

void networkTask(void* param);
void postTelemetry(const TelemetrySample& sample);
void resetWindowMetrics();

void setup(){
    Serial.begin(115200);
    Serial.println("Initializing MedVitals ESP32 Telemetry Node...");

    // initialize MAX30102 sensor
    if(!particleSensor.begin(Wire, I2C_SPEED_FAST)){
        Serial.println("MAX30102 sensor was not found. Please check wiring/power.");
        while(1) { delay(1000); }
    }

    // setup MAX30102 parameters for optical heart rate monitoring
    particleSensor.setup();
    particleSensor.setPulseAmplitudeRed(0x0A); // turn Red LED to low to indicate sensor is running
    particleSensor.setPulseAmplitudeGreen(0x00); // turn off Green LED

    // connect to Wi-Fi (auto-reconnect handled by the stack + networkTask)
    WiFi.mode(WIFI_STA);
    WiFi.setAutoReconnect(true);
    WiFi.begin(ssid, password);
    Serial.print("Connecting to Wi-Fi");
    unsigned long wifiStart = millis();
    while (WiFi.status() != WL_CONNECTED && millis() - wifiStart < 15000) {
        delay(500);
        Serial.print(".");
    }
    if (WiFi.status() == WL_CONNECTED) {
        Serial.println("\nWi-Fi connected successfully!");
        Serial.print("ESP32 IP Address: ");
        Serial.println(WiFi.localIP());
    } else {
        Serial.println("\nWi-Fi not connected yet; will keep retrying in background.");
    }

    telemetryQueue = xQueueCreate(1, sizeof(TelemetrySample));

    // network I/O on core 0; Arduino loop() (sensor sampling) stays on core 1
    xTaskCreatePinnedToCore(networkTask, "networkTask", 8192, nullptr, 1, nullptr, 0);
}

void loop() {
    uint32_t irValue = particleSensor.getIR();
    uint32_t redValue = particleSensor.getRed();

    bool fingerPresent = (irValue >= FINGER_IR_THRESHOLD);

    if (fingerPresent) {
        // Track min/max/sum for AC/DC R-ratio SpO2 estimation
        if (irValue < irMin) irMin = irValue;
        if (irValue > irMax) irMax = irValue;
        if (redValue < redMin) redMin = redValue;
        if (redValue > redMax) redMax = redValue;
        irSum += irValue;
        redSum += redValue;
        sampleCount++;

        // Pulse Detection Logic
        if (checkForBeat(irValue) == true) {
            long delta = millis() - lastBeat;
            lastBeat = millis();

            if (delta > 0) {
                float bpm = 60.0f / (delta / 1000.0f);
                if (bpm > 20.0f && bpm < 255.0f) {
                    rates[rateSpot++] = (byte)bpm;
                    rateSpot %= RATE_SIZE;
                    if (rateCount < RATE_SIZE) rateCount++;

                    // Average only over valid samples collected so far (avoids zero-dilution)
                    int sum = 0;
                    for (byte x = 0; x < rateCount; x++) {
                        sum += rates[x];
                    }
                    beatAvg = sum / rateCount;
                }
            }
        }
    } else {
        // Finger removed: clear rolling buffer so stale beats do not bleed into next placement
        rateSpot = 0;
        rateCount = 0;
        beatAvg = 0;
        spo2 = 0;
    }

    // Hand the latest sample to the network task every 2 seconds (non-blocking)
    if (millis() - lastSampleQueuedTime > transmitInterval) {
        lastSampleQueuedTime = millis();

        TelemetrySample sample;
        if (!fingerPresent || rateCount == 0) {
            // Do not fabricate "normal" vitals when sensor has no signal
            sample = { false, 0, 0 };
        } else {
            int computedSpo2 = 0;
            if (sampleCount > 0 && irSum > 0 && redSum > 0) {
                float irDc = (float)irSum / (float)sampleCount;
                float redDc = (float)redSum / (float)sampleCount;
                float irAc = (float)(irMax - irMin);
                float redAc = (float)(redMax - redMin);

                if (irAc > 0.0f && redAc > 0.0f) {
                    // Normalized AC/DC ratio R = (Red_AC / Red_DC) / (IR_AC / IR_DC)
                    // Empirical quadratic curve (Maxim AN6409); requires clinical calibration
                    float r = (redAc / redDc) / (irAc / irDc);
                    float est = -45.060f * r * r + 30.354f * r + 94.845f;
                    computedSpo2 = constrain((int)roundf(est), 50, 100);
                }
            }
            spo2 = computedSpo2;
            sample = { true, beatAvg, spo2 };
        }

        resetWindowMetrics();
        xQueueOverwrite(telemetryQueue, &sample);
    }
}

void resetWindowMetrics() {
    irMin = UINT32_MAX;
    irMax = 0;
    redMin = UINT32_MAX;
    redMax = 0;
    irSum = 0;
    redSum = 0;
    sampleCount = 0;
}

// Runs on core 0: owns Wi-Fi reconnects and HTTP so the sensor loop never blocks.
void networkTask(void* param) {
    unsigned long lastReconnectAttempt = 0;
    TelemetrySample sample;

    for (;;) {
        if (WiFi.status() != WL_CONNECTED) {
            if (millis() - lastReconnectAttempt > wifiReconnectInterval) {
                lastReconnectAttempt = millis();
                Serial.println("Wi-Fi disconnected. Attempting reconnect...");
                WiFi.reconnect();
            }
            vTaskDelay(pdMS_TO_TICKS(250));
            continue;
        }

        // wait for the next sample from loop()
        if (xQueueReceive(telemetryQueue, &sample, pdMS_TO_TICKS(500)) == pdTRUE) {
            postTelemetry(sample);
        }
    }
}

void postTelemetry(const TelemetrySample& sample) {
#if USE_TLS
    WiFiClientSecure client;
    client.setCACert(gatewayRootCa);
#else
    WiFiClient client;
#endif

    HTTPClient http;
    http.setConnectTimeout(httpTimeoutMs);
    http.setTimeout(httpTimeoutMs);

    if (!http.begin(client, gatewayUrl)) {
        Serial.println("HTTP begin failed (bad URL?)");
        return;
    }
    http.addHeader("Content-Type", "application/json");
    http.addHeader("X-Api-Key", deviceApiKey);

    // Only include sensors actually measured by this node (HR and optional SpO2).
    // Unmeasured vitals (systolicBp, respiratoryRate, temperatureCelsius) are omitted
    // rather than fabricated, so the backend treats them as null.
    String jsonPayload = String("{\"patientId\":\"") + patientId + "\","
                         "\"bedNumber\":\"" + bedNumber + "\","
                         "\"signalValid\":" + (sample.signalValid ? "true" : "false");

    if (sample.signalValid) {
        jsonPayload += ",\"heartRate\":" + String(sample.heartRate);
        if (sample.spo2 > 0) {
            jsonPayload += ",\"oxygenSaturation\":" + String(sample.spo2);
        }
    }
    jsonPayload += "}";

    Serial.print("Transmitting Telemetry Payload: ");
    Serial.println(jsonPayload);

    int httpResponseCode = http.POST(jsonPayload);

    if (httpResponseCode >= 200 && httpResponseCode < 300) {
        Serial.print("HTTP POST Success, Response Code: ");
        Serial.println(httpResponseCode);
    } else if (httpResponseCode > 0) {
        Serial.print("HTTP POST rejected, Response Code: ");
        Serial.println(httpResponseCode);
    } else {
        Serial.print("HTTP POST Failed, Error: ");
        Serial.println(http.errorToString(httpResponseCode));
    }

    http.end();
}
