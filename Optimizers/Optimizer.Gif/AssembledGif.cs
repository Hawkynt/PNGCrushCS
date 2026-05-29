using System.Drawing;
using Hawkynt.GifFileFormat;

namespace Optimizer.Gif;

internal sealed class AssembledGif {
  public Dimensions LogicalScreenSize { get; init; }
  public byte BackgroundColorIndex { get; init; }
  public Color[]? GlobalColorTable { get; init; }
  public LoopCount LoopCount { get; init; }
  public AssembledFrame[] Frames { get; init; } = [];
}
