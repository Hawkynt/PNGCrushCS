using System;

namespace FileFormat.Core;

/// <summary>Capability flags describing what a format supports, used for filtering conversion targets.</summary>
[Flags]
public enum FormatCapability {
  None = 0,
  VariableResolution = 1,
  MonochromeOnly = 2,
  IndexedOnly = 4,
  HasDedicatedOptimizer = 8,
  MultiImage = 16,
}
