using System;

namespace FileFormat.Avif.Codec;

/// <summary>AV1 7.12.2: the dequantiser step sizes a quantiser index maps to.</summary>
internal static class Av1Quantizer {

  /// <summary>DC step size for a quantiser index, clamped to the coded range as libaom does.</summary>
  public static int DcQ(int qIndex, int bitDepth) => bitDepth switch {
    8 => Av1QuantizerTables.DcQLookup8[Math.Clamp(qIndex, 0, 255)],
    10 => Av1QuantizerTables.DcQLookup10[Math.Clamp(qIndex, 0, 255)],
    12 => Av1QuantizerTables.DcQLookup12[Math.Clamp(qIndex, 0, 255)],
    _ => throw new NotSupportedException($"AV1: {bitDepth}-bit samples are not supported."),
  };

  /// <summary>AC step size for a quantiser index.</summary>
  public static int AcQ(int qIndex, int bitDepth) => bitDepth switch {
    8 => Av1QuantizerTables.AcQLookup8[Math.Clamp(qIndex, 0, 255)],
    10 => Av1QuantizerTables.AcQLookup10[Math.Clamp(qIndex, 0, 255)],
    12 => Av1QuantizerTables.AcQLookup12[Math.Clamp(qIndex, 0, 255)],
    _ => throw new NotSupportedException($"AV1: {bitDepth}-bit samples are not supported."),
  };
}
