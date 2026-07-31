using System;
using System.Collections.Generic;
using FileFormat.Core;
using FileFormat.Graph2Font;

namespace FileFormat.Graph2FontScroll;

/// <summary>In-memory representation of a Graph2Font vertical scroll (.vsc).</summary>
/// <remarks>
/// A list of file names, one per line, and nothing else. Each names a Graph2Font project beside it,
/// and the picture is those projects stacked one above another — a scroll taller than the screen,
/// assembled from screen-sized pieces because that is what the editor could edit.
/// <para/>
/// It is the only format here whose entire content is a reference to other files. That is also why
/// it can only be read from a path: given bytes alone there is nothing to resolve the names
/// against, and the names are all there is.
/// </remarks>
public readonly record struct Graph2FontScrollFile
  : IImageFormatReader<Graph2FontScrollFile>, IImageToRawImage<Graph2FontScrollFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = Graph2FontFile.Width;

  /// <summary>Rows one named project contributes.</summary>
  public const int FrameHeight = Graph2FontFile.Height;

  static string IImageFormatMetadata<Graph2FontScrollFile>.PrimaryExtension => ".vsc";
  static string[] IImageFormatMetadata<Graph2FontScrollFile>.FileExtensions => [".vsc"];
  static Graph2FontScrollFile IImageFormatReader<Graph2FontScrollFile>.FromSpan(ReadOnlySpan<byte> data)
    => Graph2FontScrollReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<Graph2FontScrollFile>.VideoModes => [
    new("Vertical scroll", [(Width, IntegerRange.Any)], [256])
  ];

  /// <summary>The projects named by the list, in order, already unwrapped.</summary>
  public IReadOnlyList<byte[]> Frames { get; init; }

  public static RawImage ToRawImage(Graph2FontScrollFile file) {
    var frames = file.Frames ?? [];
    var height = frames.Count * FrameHeight;
    var frame = new byte[Width * height];

    for (var i = 0; i < frames.Count; ++i)
      Graph2FontFile.Render(frames[i], frame, i * FrameHeight, Width);

    return new() {
      Width = Width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }
}
