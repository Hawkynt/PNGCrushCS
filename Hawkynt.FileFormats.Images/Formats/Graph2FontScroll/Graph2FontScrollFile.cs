using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Graph2Font;

namespace FileFormat.Graph2FontScroll;

/// <summary>In-memory representation of a Graph2Font vertical scroll (.vsc).</summary>
/// <remarks>
/// A list of file names, one per line, and nothing else. Each names a Graph2Font project beside it,
/// and the picture is those projects stacked one above another — a scroll taller than the screen,
/// assembled from screen-sized pieces because that is what the editor could edit.
/// <para/>
/// It is the only format here whose entire content is a reference to other files. Given bytes alone
/// there is nothing to resolve the names against, so reading by bytes returns the names and no
/// picture; the read and the write that name a file are the ones that can find, and put, the
/// projects the names point at.
/// </remarks>
public readonly record struct Graph2FontScrollFile
  : IImageFormatReader<Graph2FontScrollFile>, IImageToRawImage<Graph2FontScrollFile>,
    IImageFromRawImage<Graph2FontScrollFile>, IImageFormatWriter<Graph2FontScrollFile> {

  /// <summary>Pixels across.</summary>
  public const int Width = Graph2FontFile.Width;

  /// <summary>Rows one named project contributes.</summary>
  public const int FrameHeight = Graph2FontFile.Height;

  /// <summary>
  /// Screens a scroll may stack. Nothing in the format states a limit; this is the point past which
  /// a declared size stops being a picture anything would open.
  /// </summary>
  private const int _MAXIMUM_FRAMES = 256;

  static string IImageFormatMetadata<Graph2FontScrollFile>.PrimaryExtension => ".vsc";
  static string[] IImageFormatMetadata<Graph2FontScrollFile>.FileExtensions => [".vsc"];
  static Graph2FontScrollFile IImageFormatReader<Graph2FontScrollFile>.FromSpan(ReadOnlySpan<byte> data)
    => Graph2FontScrollReader.FromSpan(data);

  /// <summary>The names are only a picture once something has resolved them, which needs the path.</summary>
  static Graph2FontScrollFile IImageFormatReader<Graph2FontScrollFile>.FromFile(FileInfo file)
    => Graph2FontScrollReader.FromFile(file);

  static byte[] IImageFormatWriter<Graph2FontScrollFile>.ToBytes(Graph2FontScrollFile file)
    => Graph2FontScrollWriter.ToBytes(file);
  static void IImageFormatWriter<Graph2FontScrollFile>.WriteCompanions(Graph2FontScrollFile file, FileInfo target)
    => Graph2FontScrollWriter.WriteCompanions(file, target);
  static VideoMode[] IImageFormatMetadata<Graph2FontScrollFile>.VideoModes => [
    new("Vertical scroll", [(Width, new IntegerRange(FrameHeight, FrameHeight * _MAXIMUM_FRAMES, FrameHeight))], [256])
  ];

  /// <summary>The projects named by the list, in order, already unwrapped.</summary>
  public IReadOnlyList<byte[]> Frames { get; init; }

  /// <summary>The names themselves, in order, which are the only thing the .vsc file holds.</summary>
  public IReadOnlyList<string> Names { get; init; }

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

  /// <summary>Builds a scroll around a picture, under a stem no file name has offered.</summary>
  public static Graph2FontScrollFile FromRawImage(RawImage image)
    => Graph2FontScrollWriter.Encode(image, Graph2FontScrollWriter.DefaultStem);

  /// <summary>Builds a scroll whose projects are named after the scroll itself.</summary>
  /// <remarks>
  /// The names have to be decided before the bytes are, because the bytes are the names — which is
  /// why this is the overload that matters here rather than a convenience. A scroll written without
  /// a path to take its stem from still names its projects something, and whether those exist is
  /// then the caller's business.
  /// </remarks>
  public static Graph2FontScrollFile FromRawImage(RawImage image, FileInfo target)
    => Graph2FontScrollWriter.Encode(image, Graph2FontScrollWriter.StemFor(target));
}
