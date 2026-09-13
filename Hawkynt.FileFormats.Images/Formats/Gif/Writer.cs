using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Gif;

/// <summary>Convenience alias for <see cref="GifWriter"/> matching the external
/// <c>Hawkynt.GifFileFormat.Writer</c> API so consumers can migrate by namespace swap.</summary>
public static class Writer {

  /// <inheritdoc cref="GifWriter.ToBytes(GifFile)"/>
  public static byte[] ToBytes(GifFile file) => GifWriter.ToBytes(file);

  /// <inheritdoc cref="GifWriter.ToBytes(GifFile, GifWriteOptions)"/>
  public static byte[] ToBytes(GifFile file, GifWriteOptions? options) => GifWriter.ToBytes(file, options);

  /// <inheritdoc cref="GifWriter.WriteTo(GifFile, Stream)"/>
  public static void WriteTo(GifFile file, Stream output) => GifWriter.WriteTo(file, output);

  /// <inheritdoc cref="GifWriter.WriteTo(GifFile, Stream, GifWriteOptions)"/>
  public static void WriteTo(GifFile file, Stream output, GifWriteOptions? options) => GifWriter.WriteTo(file, output, options);

  /// <summary>Writes a GIF from a frame sequence without ever holding more than one frame, the shape
  /// an animation that is generated rather than loaded needs. See <see cref="GifStreamWriter"/>.</summary>
  /// <param name="output">Destination stream; not disposed here.</param>
  /// <param name="size">Logical screen size.</param>
  /// <param name="frames">Frames, consumed lazily.</param>
  /// <param name="loopCount">NETSCAPE2.0 loop count; omitted from the file when not present.</param>
  /// <param name="backgroundColorIndex">Logical Screen Descriptor background index.</param>
  /// <param name="colorResolution">Logical Screen Descriptor colour-resolution field.</param>
  /// <param name="globalColorTable">Packed RGB triplets, padded on write; <c>null</c> for none.</param>
  /// <param name="options">Encoder options; defaults to <see cref="GifWriteOptions.Default"/>.</param>
  public static void WriteTo(
    Stream output,
    Dimensions size,
    IEnumerable<Frame> frames,
    LoopCount loopCount,
    byte backgroundColorIndex = 0,
    ColorResolution colorResolution = ColorResolution.Colored256,
    byte[]? globalColorTable = null,
    GifWriteOptions? options = null) {
    ArgumentNullException.ThrowIfNull(output);
    ArgumentNullException.ThrowIfNull(frames);

    var lsd = new GifLogicalScreenDescriptor(
      Width: size.Width,
      Height: size.Height,
      HasGlobalColorTable: globalColorTable is { Length: > 0 },
      ColorResolution: (byte)((byte)colorResolution + 1),
      GlobalColorTableSorted: false,
      GlobalColorTableSize: 0,
      BackgroundColorIndex: backgroundColorIndex,
      PixelAspectRatio: 0);

    using var writer = new GifStreamWriter(output, lsd, globalColorTable, loopCount, GifVersion.Gif89a, options);
    foreach (var frame in frames)
      writer.WriteFrame(frame);
    writer.Complete();
  }

  /// <summary>Writes a GIF from a frame sequence into <paramref name="file"/>, streaming one frame at
  /// a time. The file is created or truncated.</summary>
  /// <inheritdoc cref="WriteTo(Stream, Dimensions, IEnumerable{Frame}, LoopCount, byte, ColorResolution, byte[], GifWriteOptions)"/>
  public static void ToFile(
    FileInfo file,
    Dimensions size,
    IEnumerable<Frame> frames,
    LoopCount loopCount,
    byte backgroundColorIndex = 0,
    ColorResolution colorResolution = ColorResolution.Colored256,
    byte[]? globalColorTable = null,
    GifWriteOptions? options = null) {
    ArgumentNullException.ThrowIfNull(file);
    ArgumentNullException.ThrowIfNull(frames);
    using var stream = file.Open(FileMode.Create, FileAccess.Write, FileShare.None);
    WriteTo(stream, size, frames, loopCount, backgroundColorIndex, colorResolution, globalColorTable, options);
  }
}
