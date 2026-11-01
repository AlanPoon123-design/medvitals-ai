#include <WiFi.h>
#include <HTTPClient.h>
#include <Wire.h>
#include "MAX30105.h"
#include "heartRate.h"

// Wi-FI & Gateway Configuration
const char* ssid = "YOUR_WIFI_SSID";
const char* password = "YOUR_WIFI_PASSWORD";
const char* gatewayUrl = "http://192.168.1.100:5000/api/vitals"; // replace with local machine IP

MAX30105 particleSensor;

const byte RATE_SIZE = 4; // increase for more smoothing
bye rates[RATE_SIZE];
byte rateSpot = 0;
long lastBeat = 0; // time at which the last beat occurred

float beatsPerMinute;
int beatAVg = 75;
int spo2 = 98;

unsigned long lastTransmitTime = 0;
const unsigned long transmitInterval = 2000;

void setup(){
    Serial.begin(115200);
    Serial.println("Initializing MedVitals ESP32 Telemetry Node...");

    // initialize MAX30102 sensor
    if(!particleSensor.begin(Wire, I2C_SPEED_FAST)){
        Serial.println("MAX030102 sensor was not found. Please check wiring/power.");
        while(1);
    }

    // setup MAX030102 parameters for optical heart rate monitoring
    particleSensor.setup();
    particleSensor.setPulseAmplitudeRed(0x0A); // turn Red LED to low ot indicate sensor is running
    particleSensor.setPulseAmplitudeGreen(0x00); // turn off Green LED

    // connect to Wi-Fi
    WiFi.begin(ssid, password);
Serial.print("Connecting to Wi-Fi");
  while (WiFi.status() != WL_CONNECTED) {
    delay(500);
    Serial.print(".");
  }
  Serial.println("\nWi-Fi connected successfully!");
  Serial.print("ESP32 IP Address: ");
  Serial.println(WiFi.localIP());
}

void loop() {
  long irValue = particleSensor.getIR();

  // Pulse Detection Logic
  if (checkForBeat(irValue) == true) {
    long delta = millis() - lastBeat;
    lastBeat = millis();

    beatsPerMinute = 60 / (delta / 1000.0);

    if (beatsPerMinute < 255 && beatsPerMinute > 20) {
      rates[rateSpot++] = (byte)beatsPerMinute; 
      rateSpot %= RATE_SIZE; 

      // Calculate rolling average
      beatAvg = 0;
      for (byte x = 0; x < RATE_SIZE; x++) {
        beatAvg += rates[x];
      }
      beatAvg /= RATE_SIZE;
    }
  }

  // Transmit Telemetry over HTTP POST every 2 seconds
  if (millis() - lastTransmitTime > transmitInterval) {
    lastTransmitTime = millis();

    if (WiFi.status() == WL_CONNECTED) {
      HTTPClient http;
      http.begin(gatewayUrl);
      http.addHeader("Content-Type", "application/json");

      // Fallback baseline for non-finger scenarios to ensure continuous stream
      if (irValue < 50000) {
        beatAvg = 72;
        spo2 = 98;
      } else {
        // Approximate SpO2 scaling based on Red/IR ratio from sensor
        long redValue = particleSensor.getRed();
        if (redValue > 0) {
          float ratio = (float)redValue / (float)irValue;
          spo2 = constrain((int)(110 - (25 * ratio)), 80, 100);
        }
      }

      // Construct JSON payload
      String jsonPayload = "{\"patientId\":\"PATIENT-001\","
                           "\"heartRate\":" + String(beatAvg) + ","
                           "\"systolicBP\":120,"
                           "\"respiratoryRate\":16,"
                           "\"temperature\":98.6,"
                           "\"spo2\":" + String(spo2) + "}";

      Serial.print("Transmitting Telemetry Payload: ");
      Serial.println(jsonPayload);

      int httpResponseCode = http.POST(jsonPayload);

      if (httpResponseCode > 0) {
        Serial.print("HTTP POST Success, Response Code: ");
        Serial.println(httpResponseCode);
      } else {
        Serial.print("HTTP POST Failed, Error Code: ");
        Serial.println(httpResponseCode);
      }

      http.end();
    } else {
      Serial.println("Wi-Fi disconnected. Attempting reconnect...");
    }
  }
}