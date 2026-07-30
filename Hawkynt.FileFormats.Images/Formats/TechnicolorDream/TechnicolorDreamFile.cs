using System;
using FileFormat.Core;

namespace FileFormat.TechnicolorDream;

/// <summary>In-memory representation of a Technicolor Dream picture (.lum).</summary>
/// <remarks>
/// Half a picture. A .lum file holds the Graphics 9 luminance field and nothing else; the hues live
/// in a .col file of the same name beside it, and only the pair is the picture the artist drew.
/// Splitting them that way let the program treat luminance and hue as separate drawings, which is
/// how it got colours the Atari cannot hold in registers at all.
/// <para/>
/// A .lum on its own is still a picture, and the reader draws it: a grey ramp, each of its 119 rows
/// shown twice to fill the 238 the pair would occupy. Reading the file from disk picks the .col
/// up automatically; reading it from bytes cannot, because there is nowhere to look.
/// </remarks>
public readonly record struct TechnicolorDreamFile
  : IImageFormatReader<TechnicolorDreamFile>, IImageToRawImage<TechnicolorDreamFile> {

  /// <summary>Screen pixels across.</summary>
  public const int Width = 320;

  /// <summary>Rows the pair occupies: each stored row is shown twice.</summary>
  public const int Height = 238;

  /// <summary>Rows either field stores.</summary>
  public const int StoredRows = Height / 2;

  /// <summary>Bytes one row occupies: a nibble per four pixels.</summary>
  public const int Stride = Width / 8;

  /// <summary>Offset of the field, after a short header the picture does not use.</summary>
  public const int FieldOffset = 6;

  /// <summary>Total file size.</summary>
  public const int FileSize = FieldOffset + Stride * StoredRows;

  static string IImageFormatMetadata<TechnicolorDreamFile>.PrimaryExtension => ".lum";
  static string[] IImageFormatMetadata<TechnicolorDreamFile>.FileExtensions => [".lum"];
  static TechnicolorDreamFile IImageFormatReader<TechnicolorDreamFile>.FromSpan(ReadOnlySpan<byte> data)
    => TechnicolorDreamReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<TechnicolorDreamFile>.VideoModes => [
    new("Technicolor Dream", [(Width, Height)], [256])
  ];

  /// <summary>The luminance field.</summary>
  public byte[] Luminances { get; init; }

  /// <summary>The hue field from the companion .col file, or null when there was none.</summary>
  public byte[]? Hues { get; init; }

  public static RawImage ToRawImage(TechnicolorDreamFile file) {
    var luminances = file.Luminances ?? [];
    var frame = new byte[Width * Height];

    // The odd rows always come from the luminance field.
    Atari8BitGraphics.DecodeGr9Into(luminances, FieldOffset, Stride, frame, Width, Width * 2, Width, StoredRows, 0);

    if (file.Hues is { } hues) {
      Atari8BitGraphics.BlendGr11Into(hues, FieldOffset, Stride, frame, Width, Height, 0);
    } else {
      // Without the hues there is nothing to interleave, so the luminances fill both halves and
      // the picture is the grey ramp doubled rather than every other row left black.
      Atari8BitGraphics.DecodeGr9Into(luminances, FieldOffset, Stride, frame, 0, Width * 2, Width, StoredRows, 0);
    }

    return new() {
      Width = Width,
      Height = Height,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.ApplyPalette(frame),
    };
  }
}
