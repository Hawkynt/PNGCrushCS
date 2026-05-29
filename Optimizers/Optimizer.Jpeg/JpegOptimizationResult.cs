using System;
using FileFormat.Jpeg;

namespace Optimizer.Jpeg;

public readonly record struct JpegOptimizationResult(
  JpegMode Mode,
  bool OptimizeHuffman,
  bool StripMetadata,
  bool IsLossy,
  int Quality,
  JpegSubsampling Subsampling,
  long CompressedSize,
  TimeSpan ProcessingTime,
  byte[] FileContents
) {
  public override string ToString() =>
    $"Size: {this.CompressedSize} bytes, " +
    $"Mode: {this.Mode}, " +
    $"OptHuffman: {this.OptimizeHuffman}, " +
    $"Strip: {this.StripMetadata}, " +
    (this.IsLossy ? $"Quality: {this.Quality}, Subsampling: {this.Subsampling}, " : "") +
    $"Time: {this.ProcessingTime.TotalMilliseconds:F0}ms";
}
