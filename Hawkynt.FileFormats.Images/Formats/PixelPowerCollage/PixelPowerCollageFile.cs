using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.PixelPowerCollage;

/// <summary>In-memory representation of a Pixel Power Collage picture.</summary>
/// <remarks>
/// This one authenticates against its own name. The first thirty-two bytes of the file hold the name
/// the file is meant to be saved under, terminated by a zero, and a reader compares them against the
/// name the file actually has — case does not matter, the extension does. A file renamed is a file
/// refused, which for a broadcast graphics system whose stills are addressed by name from a playout
/// list is not eccentric but the point: the name is part of the record, and a still that has been
/// renamed is no longer the still that was scheduled.
/// <para/>
/// So this is one of the few formats here that cannot be read from bytes alone, and the
/// <see cref="IImageFormatReader{TSelf}.FromSpan"/> entry says so rather than quietly skipping the
/// check. Everything after those thirty-two bytes is perfectly readable without a name; what is not
/// readable is whether the file is the one it claims to be, and answering that question wrongly is
/// worse than declining it.
/// <para/>
/// The four extensions select nothing. The layout comes from a code at 0x40 — thirty-two, twenty-four
/// or eight bits a pixel — and a file under any of the four names takes the same path.
/// </remarks>
public readonly record struct PixelPowerCollageFile : IImageFormatReader<PixelPowerCollageFile>, IImageToRawImage<PixelPowerCollageFile> {

  static string IImageFormatMetadata<PixelPowerCollageFile>.PrimaryExtension => ".i17";
  static string[] IImageFormatMetadata<PixelPowerCollageFile>.FileExtensions => [".i17", ".i18", ".ib7", ".if9"];

  /// <summary>Refuses, a picture that authenticates against its name not being readable without one.</summary>
  static PixelPowerCollageFile IImageFormatReader<PixelPowerCollageFile>.FromSpan(ReadOnlySpan<byte> data)
    => PixelPowerCollageReader.FromSpan(data);

  /// <summary>Reads a named file, which is the only way the name in it can be checked.</summary>
  static PixelPowerCollageFile IImageFormatReader<PixelPowerCollageFile>.FromFile(FileInfo file)
    => PixelPowerCollageReader.FromFile(file);

  static VideoMode[] IImageFormatMetadata<PixelPowerCollageFile>.VideoModes => [
    new("Collage", [(IntegerRange.Any, IntegerRange.Any)]),
  ];

  /// <summary>Bytes at the head of the file holding the name it must be saved under.</summary>
  public const int NameSize = 32;

  /// <summary>Where the picture itself begins.</summary>
  public const int PixelOffset = 0x80;

  /// <summary>Largest picture either way round.</summary>
  public const int MaximumExtent = 599999;

  public int Width { get; init; }

  public int Height { get; init; }

  /// <summary>Thirty-two, twenty-four or eight.</summary>
  public int BitsPerPixel { get; init; }

  /// <summary>The picture as it lies, from the top-left corner, with no padding at the row end.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Bytes a row takes: no padding, so exactly as many as the pixels need.</summary>
  public int Stride => this.Width * this.BitsPerPixel / 8;

  public static RawImage ToRawImage(PixelPowerCollageFile file) => file.BitsPerPixel switch {
    8 => new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Gray8,
      PixelData = file.PixelData[..],
    },
    // Blue first, which is the order a Windows bitmap keeps and this one keeps too.
    24 => new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Bgr24,
      PixelData = file.PixelData[..],
    },
    _ => new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgba32,
      PixelData = _AlphaFirstToRgba(file.PixelData),
    },
  };

  /// <summary>
  /// Turns the thirty-two-bit layout round: the file keeps alpha first and then blue, green, red.
  /// </summary>
  /// <remarks>
  /// Not the order the rest of the tool uses — a thirty-two-bit Windows bitmap handed to the same
  /// converter comes out blue, green, red, alpha, and this comes out the other way about. Read as a
  /// Windows bitmap the picture keeps its green and swaps red for blue while the alpha becomes the
  /// blue channel, which on a still with a soft edge is a coloured fringe rather than an obvious fault.
  /// </remarks>
  private static byte[] _AlphaFirstToRgba(byte[] pixels) {
    var rgba = new byte[pixels.Length];
    for (var at = 0; at + 3 < pixels.Length; at += 4) {
      rgba[at] = pixels[at + 3];
      rgba[at + 1] = pixels[at + 2];
      rgba[at + 2] = pixels[at + 1];
      rgba[at + 3] = pixels[at];
    }

    return rgba;
  }
}
