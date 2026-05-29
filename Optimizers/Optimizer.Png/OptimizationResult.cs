using System;
using FileFormat.Png;

namespace Optimizer.Png;

/// <summary>Represents an optimization result for comparison</summary>
public readonly record struct OptimizationResult(
  PngColorType ColorMode,
  int BitDepth,
  PngInterlaceMethod InterlaceMethod,
  FilterStrategy FilterStrategy,
  DeflateMethod DeflateMethod,
  long CompressedSize,
  PngFilterType[] Filters,
  int FilterTransitions,
  TimeSpan ProcessingTime,
  byte[] FileContents,
  QuantizerDithererCombo? LossyPaletteCombo = null
) {
  public override string ToString() {
    return $"Size: {this.CompressedSize} bytes, " +
           $"ColorMode: {this.ColorMode}, " +
           $"BitDepth: {this.BitDepth}, " +
           $"Interlace: {this.InterlaceMethod}, " +
           $"Filter: {this.FilterStrategy}, " +
           $"Deflate: {this.DeflateMethod}, " +
           $"Transitions: {this.FilterTransitions}, " +
           (this.LossyPaletteCombo.HasValue
             ? $"Quantizer: {this.LossyPaletteCombo.Value.QuantizerName}+{this.LossyPaletteCombo.Value.DithererName}, "
             : "") +
           $"Time: {this.ProcessingTime.TotalMilliseconds:F0}ms";
  }
}
