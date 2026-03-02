using System;
using System.Drawing;
using Hawkynt.GifFileFormat;

namespace GifOptimizer;

internal sealed class AssembledFrame {
  public byte[] CompressedData { get; init; } = [];
  public Dimensions Size { get; init; }
  public Offset Position { get; init; }
  public Color[]? LocalColorTable { get; init; }
  public TimeSpan Delay { get; init; }
  public FrameDisposalMethod DisposalMethod { get; init; }
  public byte? TransparentColorIndex { get; init; }
  public byte BitsPerPixel { get; init; } = 8;
}
