using System;
using FileFormat.Gif;
using FileFormat.Core;

namespace Optimizer.Gif;

internal sealed class AssembledFrame {

  /// <summary>The complete GIF image-data block as <see cref="FileFormat.Gif.GifLzwCodec"/> emits it:
  /// the LZW minimum code size byte, the sub-blocks, and the zero-length terminator.</summary>
  public byte[] CompressedData { get; init; } = [];
  public Dimensions Size { get; init; }
  public Offset Position { get; init; }
  public Rgba32[]? LocalColorTable { get; init; }
  public TimeSpan Delay { get; init; }
  public FrameDisposalMethod DisposalMethod { get; init; }
  public byte? TransparentColorIndex { get; init; }
}
