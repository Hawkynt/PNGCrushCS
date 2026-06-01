using System;

namespace FileFormat.Core;

/// <summary>
/// Capability flags describing what a format supports.
/// </summary>
/// <remarks>
/// Constraints related to dimensions, palette sizes, available palettes, and display hints live inside
/// <see cref="IImageFormatMetadata{TSelf}.VideoModes"/>. This enum only carries orthogonal flags that
/// aren't derivable from a video mode.
/// </remarks>
[Flags]
public enum FormatCapability {
  None = 0,

  /// <summary>The format has a dedicated optimizer implementation (e.g. Optimizer.Png, Optimizer.Gif)
  /// that should be preferred over generic conversion when targeting it.</summary>
  HasDedicatedOptimizer = 8,

  /// <summary>The format file contains multiple images (animated GIF, multi-page TIFF, ICO sets, APNG, MNG, FLI).</summary>
  MultiImage = 16,
}
