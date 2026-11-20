"""
Validates an ONNX model against the contract the Java analytics engine relies on
(OnnxInferenceService): input 'vitals_sequence' of shape [batch, seq_len, 5] (float32),
first output of shape [batch, 1] with probabilities in [0, 1].

Usage: python verify_model.py <model.onnx> [<model.onnx> ...]
"""
import sys

import numpy as np
import onnx
import onnxruntime as ort

EXPECTED_INPUT = "vitals_sequence"
FEATURES = 5


def verify(path: str) -> None:
    model = onnx.load(path, load_external_data=False)
    # the Java engine loads the model as one classpath resource, so weights must be embedded
    external = [t.name for t in model.graph.initializer
                if t.data_location == onnx.TensorProto.EXTERNAL]
    assert not external, f"{path}: weights stored in external data file(s): {external[:3]}"
    onnx.checker.check_model(model)

    session = ort.InferenceSession(path, providers=["CPUExecutionProvider"])
    inputs = session.get_inputs()
    assert len(inputs) == 1, f"{path}: expected 1 input, got {len(inputs)}"
    assert inputs[0].name == EXPECTED_INPUT, f"{path}: input is '{inputs[0].name}', expected '{EXPECTED_INPUT}'"

    for seq_len in (1, 10, 24):  # dynamic sequence length must work
        x = np.random.default_rng(0).standard_normal((1, seq_len, FEATURES)).astype(np.float32)
        out = session.run(None, {EXPECTED_INPUT: x})[0]
        assert out.shape == (1, 1), f"{path}: output shape {out.shape}, expected (1, 1)"
        assert 0.0 <= float(out[0][0]) <= 1.0, f"{path}: output {out[0][0]} not a probability"

    # sanity check clinical directionality: normal vitals < deteriorating vitals
    normal = np.tile(np.array([75.0, 120.0, 15.0, 36.9, 98.0], dtype=np.float32), (1, 10, 1))
    critical = np.tile(np.array([135.0, 72.0, 30.0, 39.0, 84.0], dtype=np.float32), (1, 10, 1))
    p_normal = float(session.run(None, {EXPECTED_INPUT: normal})[0][0][0])
    p_critical = float(session.run(None, {EXPECTED_INPUT: critical})[0][0][0])
    assert p_normal < 0.3 and p_critical > 0.7, (
        f"{path}: unexpected probabilities (normal={p_normal:.3f}, critical={p_critical:.3f})"
    )

    print(f"OK: {path} (normal={p_normal:.3f}, critical={p_critical:.3f})")


if __name__ == "__main__":
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    for model_path in sys.argv[1:]:
        verify(model_path)
