document.addEventListener("DOMContentLoaded", () => {
    // Configurable via window.MEDVITALS_CONFIG.gatewayUrl or <meta name="medvitals-gateway-url">
    const GATEWAY_URL = window.MEDVITALS_CONFIG?.gatewayUrl
        || document.querySelector('meta[name="medvitals-gateway-url"]')?.content
        || "http://localhost:5000/api/vitals";
    // must match the gateway's Auth:DashboardApiKey. NOTE: anything shipped to a browser is
    // readable by its users, so this only keeps out casual/unauthorised clients; use real user
    // sign-in (OIDC) before exposing the dashboard beyond a trusted network.
    const DASHBOARD_API_KEY = "REPLACE_WITH_DASHBOARD_API_KEY";
    const HARDWARE_PATIENT_ID = "PATIENT-001";
    const POLL_INTERVAL_MS = 2000;
    const REQUEST_TIMEOUT_MS = 8000;
    const SCAN_REPLAY_DELAY_MS = 1500;
    const MAX_SCAN_ROWS = 10000;

    const sourceTag = document.getElementById('source-tag');
    const scanButton = document.getElementById('btn-scan');
    const statusMessage = document.getElementById('status-message');

    let pollTimer = null;
    let pollingActive = false;
    let scanning = false;

    // initialize Chart.js
    const ctx = document.getElementById('riskChart').getContext('2d');
    const riskChart = new Chart(ctx, {
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

    // ---------- UI helpers ----------

    function setSource(text) {
        sourceTag.textContent = text;
    }

    function showStatus(message, level = "error") {
        statusMessage.textContent = message;
        statusMessage.className = `status-message status-${level}`;
        statusMessage.hidden = false;
    }

    function clearStatus() {
        statusMessage.textContent = "";
        statusMessage.hidden = true;
    }

    async function fetchWithTimeout(url, options = {}) {
        const controller = new AbortController();
        const timer = setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);
        try {
            const headers = { ...(options.headers || {}), "X-Api-Key": DASHBOARD_API_KEY };
            return await fetch(url, { ...options, headers, signal: controller.signal });
        } finally {
            clearTimeout(timer);
        }
    }

    async function describeHttpError(res) {
        try {
            const body = await res.json();
            return body.detail || body.title || `HTTP ${res.status}`;
        } catch {
            return `HTTP ${res.status}`;
        }
    }

    // ---------- 1. dataset scanner ----------

    /**
     * Minimal RFC 4180 CSV parser: handles quoted fields, escaped quotes (""),
     * commas/newlines inside quotes, CRLF/LF line endings and a UTF-8 BOM.
     */
    function parseCsv(text) {
        const rows = [];
        let row = [];
        let field = "";
        let inQuotes = false;
        text = text.replace(/^\uFEFF/, "");

        for (let i = 0; i < text.length; i++) {
            const c = text[i];
            if (inQuotes) {
                if (c === '"') {
                    if (text[i + 1] === '"') { field += '"'; i++; }
                    else inQuotes = false;
                } else {
                    field += c;
                }
            } else if (c === '"') {
                inQuotes = true;
            } else if (c === ',') {
                row.push(field); field = "";
            } else if (c === '\n' || c === '\r') {
                if (c === '\r' && text[i + 1] === '\n') i++;
                row.push(field); field = "";
                rows.push(row); row = [];
            } else {
                field += c;
            }
        }
        if (field !== "" || row.length > 0) {
            row.push(field);
            rows.push(row);
        }
        return rows.filter(r => r.some(cell => cell.trim() !== ""));
    }

    function toNumber(value) {
        const trimmed = String(value ?? "").trim();
        return trimmed === "" ? NaN : Number(trimmed);
    }

    /**
     * Expected columns: heartRate, systolicBp, respiratoryRate, temperatureCelsius, oxygenSaturation.
     * A header row is detected (non-numeric first cell) and skipped.
     */
    function rowsToVitals(rows) {
        const records = [];
        const rejected = [];
        const startIdx = rows.length > 0 && Number.isNaN(toNumber(rows[0][0])) ? 1 : 0;

        for (let i = startIdx; i < rows.length; i++) {
            const lineNo = i - startIdx + 1; // 1-based data row (blank lines and header excluded)
            const cells = rows[i];
            if (cells.length < 5) {
                rejected.push(`row ${lineNo}: expected 5 columns, got ${cells.length}`);
                continue;
            }
            const [hr, sbp, rr, temp, spo2] = cells.slice(0, 5).map(toNumber);
            const ints = [hr, sbp, rr, spo2];
            if (![...ints, temp].every(Number.isFinite) || !ints.every(Number.isInteger)) {
                rejected.push(`row ${lineNo}: non-numeric or non-integer value`);
                continue;
            }
            if (temp < 25 || temp > 45) {
                rejected.push(`row ${lineNo}: temperature ${temp} is outside 25–45 °C`);
                continue;
            }
            records.push({
                patientId: `SCAN-ROW-${lineNo}`,
                bedNumber: "DATASET",
                heartRate: hr,
                systolicBp: sbp,
                respiratoryRate: rr,
                temperatureCelsius: temp,
                oxygenSaturation: spo2
            });
        }
        return { records, rejected };
    }

    function readFileAsText(file) {
        return new Promise((resolve, reject) => {
            const reader = new FileReader();
            reader.onload = () => resolve(reader.result);
            reader.onerror = () => reject(reader.error ?? new Error("Could not read file"));
            reader.readAsText(file);
        });
    }

    scanButton.addEventListener('click', async () => {
        if (scanning) return; // guard against double-clicks / overlapping scans

        const file = document.getElementById('csv-file').files[0];
        if (!file) {
            showStatus("Please select a clinical CSV dataset file to scan.", "warning");
            return;
        }

        scanning = true;
        scanButton.disabled = true;
        clearStatus();

        // pause live hardware polling during the dataset scan
        stopHardwareTelemetryStream();
        setSource("ACTIVE: DATASET SCAN RUNNING");

        try {
            const { records, rejected } = rowsToVitals(parseCsv(await readFileAsText(file)));

            if (records.length === 0) {
                showStatus(`No valid records found in file.${rejected.length ? " " + rejected.slice(0, 3).join("; ") : ""}`);
                return;
            }
            if (records.length > MAX_SCAN_ROWS) {
                showStatus(`File has ${records.length} records; the limit is ${MAX_SCAN_ROWS}.`);
                return;
            }
            if (rejected.length > 0) {
                showStatus(`Skipped ${rejected.length} invalid row(s): ${rejected.slice(0, 3).join("; ")}${rejected.length > 3 ? "…" : ""}`, "warning");
            }

            const res = await fetchWithTimeout(`${GATEWAY_URL}/batch-assess`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(records)
            });
            if (!res.ok) {
                showStatus(`Dataset scan failed: ${await describeHttpError(res)}`);
                return;
            }

            // replay results one by one so the trend chart animates
            const results = await res.json();
            for (const result of results) {
                updateDashboardUI(result);
                await new Promise(resolve => setTimeout(resolve, SCAN_REPLAY_DELAY_MS));
            }
        } catch (err) {
            console.error("Dataset scan failed", err);
            showStatus(err.name === "AbortError"
                ? "Dataset scan timed out waiting for the gateway."
                : "Dataset scan failed: backend gateway unreachable.");
        } finally {
            scanning = false;
            scanButton.disabled = false;
            startHardwareTelemetryStream();
        }
    });

    // ---------- 2. hardware telemetry polling ----------

    async function pollOnce() {
        try {
            const res = await fetchWithTimeout(`${GATEWAY_URL}/${encodeURIComponent(HARDWARE_PATIENT_ID)}`);
            if (res.ok) {
                setSource("ACTIVE: HARDWARE TELEMETRY STREAM");
                updateDashboardUI(await res.json());
            } else if (res.status === 404) {
                setSource("WAITING FOR HARDWARE TELEMETRY");
            } else if (res.status === 401 || res.status === 403) {
                setSource("UNAUTHORIZED: CHECK DASHBOARD API KEY");
            } else {
                setSource("GATEWAY ERROR");
            }
        } catch {
            setSource("OFFLINE: GATEWAY UNREACHABLE");
        }
    }

    // setTimeout chain (not setInterval) so a slow request never overlaps with the next poll
    function scheduleNextPoll() {
        if (!pollingActive) return;
        pollTimer = setTimeout(async () => {
            await pollOnce();
            scheduleNextPoll();
        }, POLL_INTERVAL_MS);
    }

    function startHardwareTelemetryStream() {
        if (pollingActive) return;
        pollingActive = true;
        setSource("CONNECTING TO GATEWAY…");
        pollOnce().then(scheduleNextPoll);
    }

    function stopHardwareTelemetryStream() {
        pollingActive = false;
        if (pollTimer) clearTimeout(pollTimer);
        pollTimer = null;
    }

    // ---------- UI update ----------

    function updateDashboardUI(data) {
        const mews = data.mewsScore ?? 0;
        document.getElementById('mews-val').textContent = mews;

        const aiPct = Math.round((data.aiRiskScore ?? 0) * 100);
        document.getElementById('ai-val').textContent = `${aiPct}%`;

        const badge = document.getElementById('escalation-badge');
        badge.textContent = data.recommendation || "LOW RISK: Standard ward monitoring";

        const triage = (data.triageLevel || "").toUpperCase();
        if (triage === "NO_SIGNAL") {
            setSource("SENSOR NO SIGNAL");
            badge.className = "alert-badge badge-medium";
            return; // do not plot a fabricated 0% risk point when sensor is detached
        }

        if (triage === "RED" || aiPct > 75 || mews >= 5) {
            badge.className = "alert-badge badge-high";
        } else if (triage === "YELLOW" || aiPct > 40 || mews >= 3) {
            badge.className = "alert-badge badge-medium";
        } else {
            badge.className = "alert-badge badge-low";
        }

        // plot trend
        const timestamp = new Date().toLocaleTimeString();
        if (riskChart.data.labels.length >= 10) {
            riskChart.data.labels.shift();
            riskChart.data.datasets[0].data.shift();
        }
        riskChart.data.labels.push(timestamp);
        riskChart.data.datasets[0].data.push(aiPct);
        riskChart.update();
    }

    startHardwareTelemetryStream();
});
