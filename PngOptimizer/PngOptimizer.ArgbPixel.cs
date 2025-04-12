using System.Runtime.InteropServices;

namespace PngOptimizer;

public sealed partial class PngOptimizer {
  [StructLayout(LayoutKind.Sequential, Size = 4)]
  private struct ArgbPixel {
    public byte B;
    public byte G;
    public byte R;
    public byte A;
  }
}
