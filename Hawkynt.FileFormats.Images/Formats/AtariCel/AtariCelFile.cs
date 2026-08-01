using System;
using FileFormat.Core;

namespace FileFormat.AtariCel;

/// <summary>In-memory representation of an Atari ST CEL picture.</summary>
/// <remarks>
/// A low-resolution ST screen of any size, behind a 128-byte header that carries the palette near
/// its front and the dimensions near its end. The two words at the very start are all ones followed
/// by all zeros, which is what tells it apart from the other formats that ended up sharing the name.
/// <para/>
/// It is a third thing called CEL: the others here are the paper-doll cells of KiSS and the
/// Autodesk Animator's frames, and none of the three can be read as either of the others.
/// </remarks>
public readonly record struct AtariCelFile
  : IImageFormatReader<AtariCelFile>, IImageToRawImage<AtariCelFile>,
    IImageFromRawImage<AtariCelFile>, IImageFormatWriter<AtariCelFile> {

  /// <summary>Bytes before the bitmap.</summary>
  internal const int HeaderSize = 128;

  /// <summary>Where the palette sits.</summary>
  internal const int PaletteOffset = 4;

  /// <summary>Colours a low-resolution screen draws from.</summary>
  internal const int PaletteColors = 16;

  /// <summary>Bitplanes a low-resolution screen spends.</summary>
  internal const int Planes = 4;

  /// <summary>Where the size sits, near the end of the header rather than its start.</summary>
  internal const int WidthOffset = 58;

  internal const int HeightOffset = 60;

  static string IImageFormatMetadata<AtariCelFile>.PrimaryExtension => ".cel";
  static string[] IImageFormatMetadata<AtariCelFile>.FileExtensions => [".cel"];
  static AtariCelFile IImageFormatReader<AtariCelFile>.FromSpan(ReadOnlySpan<byte> data) => AtariCelReader.FromSpan(data);
  static byte[] IImageFormatWriter<AtariCelFile>.ToBytes(AtariCelFile file) => AtariCelWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<AtariCelFile>.VideoModes => [
    new("Default", [(320, 200)], [PaletteColors])
  ];

  /// <summary>Whether a header is one of these, which nothing else states this way.</summary>
  public static bool MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 4 && header[0] == 0xFF && header[1] == 0xFF && header[2] == 0 && header[3] == 0;

  public int Width { get; init; }

  public int Height { get; init; }

  /// <summary>The palette as RGB triplets.</summary>
  public byte[] Palette { get; init; }

  /// <summary>One index a pixel.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Bytes one row of all four planes takes.</summary>
  internal int Stride => AtariStGraphics.BytesPerRow(this.Width, Planes);

  public static RawImage ToRawImage(AtariCelFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = file.PixelData[..],
    Palette = file.Palette[..],
    PaletteCount = PaletteColors,
  };

  public static AtariCelFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var indexed = image.EnsureIndexedAtMost(PaletteColors);
    var palette = new byte[PaletteColors * 3];
    (indexed.Palette ?? []).AsSpan(0, Math.Min(palette.Length, indexed.Palette?.Length ?? 0)).CopyTo(palette);

    return new() {
      Width = indexed.Width,
      Height = indexed.Height,
      Palette = palette,
      PixelData = indexed.PixelData[..],
    };
  }
}
