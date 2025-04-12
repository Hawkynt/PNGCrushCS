using System;

namespace PngOptimizer;

/// <summary>Represents an optimization result for comparison</summary>
public sealed record OptimizationResult {
  public required ColorMode ColorMode { get; init; }
  public required int BitDepth { get; init; }
  public required InterlaceMethod InterlaceMethod { get; init; }
  public required FilterStrategy FilterStrategy { get; init; }
  public required DeflateMethod DeflateMethod { get; init; }
  public required long CompressedSize { get; init; }
  public required byte[][] FilteredData { get; init; }
  public required FilterType[] Filters { get; init; }
  public required int FilterTransitions { get; init; }
  public required TimeSpan ProcessingTime { get; init; }
  public byte[] FileContents { get; init; }

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
