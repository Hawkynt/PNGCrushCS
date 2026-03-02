namespace PngOptimizer;

/// <summary>Identifies a quantizer/ditherer pair for lossy palette quantization</summary>
public readonly record struct QuantizerDithererCombo(string QuantizerName, string DithererName);
