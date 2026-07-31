using FileFormat.Gif;
using FileFormat.Core;

namespace Optimizer.Gif;

internal sealed class AssembledGif {
  public Dimensions LogicalScreenSize { get; init; }
  public byte BackgroundColorIndex { get; init; }
  public Rgba32[]? GlobalColorTable { get; init; }
  public LoopCount LoopCount { get; init; }
  public AssembledFrame[] Frames { get; init; } = [];
}
