using System;

namespace Optimizer.Image;

/// <summary>Result of the universal image optimization.</summary>
public readonly record struct ImageOptimizationResult(
  ImageFormat OriginalFormat,
  ImageFormat OutputFormat,
  string OutputExtension,
  long CompressedSize,
  TimeSpan ProcessingTime,
  byte[] FileContents,
  string Details
);
