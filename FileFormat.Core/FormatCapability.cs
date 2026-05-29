using System;

namespace FileFormat.Core;

/// <summary>Capability flags describing what a format supports, used for filtering conversion targets.</summary>
[Flags]
public enum FormatCapability {
  None = 0,
  /// <summary>(Legacy / informational) Format supports any image dimensions.
  /// New code should use the inverted <see cref="FixedResolution"/> flag instead — having a positive
  /// "variable" flag broke when format files declared other caps and accidentally dropped this one.</summary>
  VariableResolution = 1,
  MonochromeOnly = 2,
  IndexedOnly = 4,
  HasDedicatedOptimizer = 8,
  MultiImage = 16,
  /// <summary>The format requires specific pixel dimensions (e.g. Apple II HGR = 280x192, NES tile = 8x8).
  /// Setting this triggers a resize prompt in the Save-As flow. Default (absent) means the format is variable-resolution.</summary>
  FixedResolution = 32,
}
