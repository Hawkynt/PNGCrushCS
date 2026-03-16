using System;

namespace Optimizer.Image;

[Flags]
internal enum FormatCapability {
  None = 0,
  VariableResolution = 1,
  MonochromeOnly = 2,
  IndexedOnly = 4,
  HasDedicatedOptimizer = 8,
}
