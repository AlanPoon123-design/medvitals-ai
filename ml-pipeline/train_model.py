import torch
import torch.nn as nn
import torch.onnx

onnx_path = "deterioration_model.onnx"

# define Time-Series LSTM Model
class DeteriorationPredictor(nn.Module):
    def __init__(self, input_dim=5, hidden_dim=16, num_layers=1):
        super(DeteriorationPredictor, self).__init__()
        # input_dim = 5 vitals (HR, Systolic BP, Resp Rate, Temp, AVPU)
        self.lstm = nn.LSTM(input_dim, hidden_dim, num_layers, batch_first=True)
        self.fc = nn.Linear(hidden_dim, 1)
        self.sigmoid = nn.Sigmoid()

    def forward(self, x):
        # x shape: [batch_size, sequence_length, input_dim]
        out, _ = self.lstm(x)
        # use last time-step output
        last_step = out[:, -1, :]
        prediction = self.sigmoid(self.fc(last_step))
        return prediction

def export_to_onnx():
    model = DeteriorationPredictor()
    model.eval()

    # dummy input representing 1 patient, 10 time steps, 5 vital parameters
    # shape: (batch_size=1, sequence_length=10, features=5)
    dummy_input = torch.randn(1, 10, 5)

    torch.onnx.export(
        model,
        dummy_input,
        onnx_path,
        export_params=True,
        opset_version=14,
        do_constant_folding=True,
        inpput_names=['vitals_sequence'],
        output_names=['deterioration_risk'],
        dynamic_axes={
            'vitals_sequence': {0, 'batch_size', 1, 'sequence_length'},
            'deterioration_risk': {0: 'batch_size'}
        }
    )
    print(f"Model successfully exported to {onnx_path}")

if __name__ == "__main__":
    export_to_onnx()