using System;

namespace PngOptimizer;

/// <summary>Represents an optimization result for comparison</summary>
public record struct OptimizationResult(
  ColorMode ColorMode, 
  int BitDepth, 
  InterlaceMethod InterlaceMethod, 
  FilterStrategy FilterStrategy, 
  DeflateMethod DeflateMethod, 
  long CompressedSize, 
  byte[][] FilteredData, 
  FilterType[] Filters, 
  int FilterTransitions, 
  TimeSpan ProcessingTime, 
  byte[] FileContents
) {
  public override string ToString() =>
    $"Size: {this.CompressedSize} bytes, " +
    $"ColorMode: {this.ColorMode}, " +
    $"BitDepth: {this.BitDepth}, " +
    $"Interlace: {this.InterlaceMethod}, " +
    $"Filter: {this.FilterStrategy}, " +
    $"Deflate: {this.DeflateMethod}, " +
    $"Transitions: {this.FilterTransitions}, " +
    $"Time: {this.ProcessingTime.TotalMilliseconds:F0}ms";
}
