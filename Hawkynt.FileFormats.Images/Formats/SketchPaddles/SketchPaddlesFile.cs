using System;
using FileFormat.Core;

namespace FileFormat.SketchPaddles;

/// <summary>In-memory representation of a Sketch-PadDles picture (.skp).</summary>
/// <remarks>
/// A bare Graphics 15 screen with no header and no palette — the file is nothing but the bitmap,
/// and the four colours are the ones the program itself always worked in. That is why it can be
/// exactly 7680 bytes and still be a complete picture: everything else about it was fixed by the
/// editor rather than chosen by the artist.
/// </remarks>
public readonly record struct SketchPaddlesFile
  : IImageFormatReader<SketchPaddlesFile>, IImageToRawImage<SketchPaddlesFile> {

  /// <summary>Screen pixels across; each of the 160 logical pixels is drawn two wide.</summary>
  public const int Width = 320;

  /// <summary>Rows.</summary>
  public const int Height = 192;

  /// <summary>Bytes one row occupies.</summary>
  public const int Stride = Width / 8;

  /// <summary>Total file size.</summary>
  public const int FileSize = Stride * Height;

  /// <summary>The registers Sketch-PadDles worked in: background, PF0, PF1 and PF2.</summary>
  public static ReadOnlySpan<byte> Registers => [38, 40, 0, 12];

  static string IImageFormatMetadata<SketchPaddlesFile>.PrimaryExtension => ".skp";
  static string[] IImageFormatMetadata<SketchPaddlesFile>.FileExtensions => [".skp"];
  static SketchPaddlesFile IImageFormatReader<SketchPaddlesFile>.FromSpan(ReadOnlySpan<byte> data)
    => SketchPaddlesReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<SketchPaddlesFile>.VideoModes => [
    new("Sketch-PadDles", [(Width, Height)], [4])
  ];

  /// <summary>The bitmap.</summary>
  public byte[] Data { get; init; }

  public static RawImage ToRawImage(SketchPaddlesFile file) => new() {
    Width = Width,
    Height = Height,
    Format = PixelFormat.Rgb24,
    PixelData = Atari8BitGraphics.DecodeGr15Frame(file.Data ?? [], 0, Stride, Width, Height, Registers),
  };
}
