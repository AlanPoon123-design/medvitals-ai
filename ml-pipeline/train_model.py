import sys
import numpy as np
import torch
import torch.nn as nn
import torch.onnx
from torch.export import Dim
from torch.utils.data import DataLoader, TensorDataset

onnx_path = "deterioration_model.onnx"

# Feature order must match PatientVitalsDto.historicalVitals in Java:
#   [heartRate (bpm), systolicBp (mmHg), respiratoryRate (br/min), temperatureCelsius (°C), oxygenSaturation (SpO2 %)]
FEATURE_MEANS = torch.tensor([80.0, 120.0, 16.0, 37.0, 97.0])
FEATURE_STDS = torch.tensor([25.0, 25.0, 6.0, 1.2, 4.0])


# define Time-Series LSTM Model
class DeteriorationPredictor(nn.Module):
    def __init__(self, input_dim=5, hidden_dim=16, num_layers=1):
        super().__init__()
        # input_dim = 5 vitals: HR, Systolic BP, Resp Rate, Temp (°C), SpO2 (%)
        self.lstm = nn.LSTM(input_dim, hidden_dim, num_layers, batch_first=True)
        self.fc = nn.Linear(hidden_dim, 1)
        self.sigmoid = nn.Sigmoid()

    def forward(self, x):
        # x shape: [batch_size, sequence_length, input_dim]
        # normalize inside the graph so callers can pass raw clinical units
        x_norm = (x - FEATURE_MEANS) / FEATURE_STDS
        out, _ = self.lstm(x_norm)
        # use last time-step output
        last_step = out[:, -1, :]
        return self.sigmoid(self.fc(last_step))


def generate_synthetic_dataset(num_samples=1200, seq_len=10, seed=42):
    """
    Generates labeled physiological time-series sequences for training.
    NOTE: Synthetic data for prototype/CI verification; replace with validated
    clinical cohort data (e.g. MIMIC-IV) before clinical deployment.
    """
    rng = np.random.default_rng(seed)
    X = np.zeros((num_samples, seq_len, 5), dtype=np.float32)
    y = np.zeros((num_samples, 1), dtype=np.float32)

    for i in range(num_samples):
        deteriorating = i % 2 == 1
        t = np.linspace(0.0, 1.0, seq_len, dtype=np.float32)

        if deteriorating:
            # rising HR & RR, falling SBP & SpO2, fever or hypothermia trend
            hr = 85.0 + 45.0 * t + rng.normal(0, 4, seq_len)
            sbp = 115.0 - 40.0 * t + rng.normal(0, 5, seq_len)
            rr = 16.0 + 14.0 * t + rng.normal(0, 2, seq_len)
            temp = (38.8 if i % 4 == 1 else 34.7) + rng.normal(0, 0.25, seq_len)
            spo2 = 96.0 - 11.0 * t + rng.normal(0, 1.5, seq_len)
            y[i, 0] = 1.0
        else:
            hr = 74.0 + rng.normal(0, 6, seq_len)
            sbp = 122.0 + rng.normal(0, 7, seq_len)
            rr = 15.0 + rng.normal(0, 2, seq_len)
            temp = 36.9 + rng.normal(0, 0.25, seq_len)
            spo2 = 98.0 + rng.normal(0, 1.0, seq_len)
            y[i, 0] = 0.0

        X[i, :, 0] = np.clip(hr, 30, 220)
        X[i, :, 1] = np.clip(sbp, 50, 240)
        X[i, :, 2] = np.clip(rr, 5, 45)
        X[i, :, 3] = np.clip(temp, 32.0, 42.0)
        X[i, :, 4] = np.clip(spo2, 65, 100)

    return torch.from_numpy(X), torch.from_numpy(y)


def train_model(epochs=25, batch_size=64, lr=1e-2):
    torch.manual_seed(42)
    X, y = generate_synthetic_dataset()
    split = int(0.8 * len(X))
    train_loader = DataLoader(TensorDataset(X[:split], y[:split]), batch_size=batch_size, shuffle=True)
    X_val, y_val = X[split:], y[split:]

    model = DeteriorationPredictor()
    criterion = nn.BCELoss()
    optimizer = torch.optim.Adam(model.parameters(), lr=lr)

    for epoch in range(1, epochs + 1):
        model.train()
        total_loss = 0.0
        for xb, yb in train_loader:
            optimizer.zero_grad()
            preds = model(xb)
            loss = criterion(preds, yb)
            loss.backward()
            optimizer.step()
            total_loss += loss.item() * len(xb)

        if epoch == 1 or epoch == epochs:
            model.eval()
            with torch.no_grad():
                val_preds = model(X_val)
                val_loss = criterion(val_preds, y_val).item()
                val_acc = ((val_preds >= 0.5).float() == y_val).float().mean().item()
            print(f"Epoch {epoch:2d}/{epochs} | train_loss={total_loss / split:.4f} | val_loss={val_loss:.4f} | val_acc={val_acc:.3f}")

    return model


def export_to_onnx(output_path=onnx_path):
    model = train_model()
    model.eval()

    # dummy input representing 1 patient, 10 time steps, 5 vital parameters
    # shape: (batch_size=1, sequence_length=10, features=5)
    dummy_input = torch.tensor([[[75.0, 120.0, 16.0, 37.0, 98.0]] * 10], dtype=torch.float32)

    torch.onnx.export(
        model,
        (dummy_input,),
        output_path,
        # dynamo exporter (torch>=2.9 default, needs onnxscript) emits opset 18 natively
        dynamo=True,
        opset_version=18,
        # embed weights in the .onnx file: the Java engine loads the model as one classpath resource
        external_data=False,
        input_names=['vitals_sequence'],
        output_names=['deterioration_risk'],
        dynamic_shapes={
            'x': {0: Dim('batch_size'), 1: Dim('sequence_length')}
        }
    )
    print(f"Model successfully exported to {output_path}")


if __name__ == "__main__":
    export_to_onnx(sys.argv[1] if len(sys.argv) > 1 else onnx_path)
