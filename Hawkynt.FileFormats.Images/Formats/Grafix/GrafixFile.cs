using System;
using FileFormat.Core;

namespace FileFormat.Grafix;

/// <summary>In-memory representation of a Grafix picture (.grx).</summary>
/// <remarks>
/// An Atari ST picture with a GEM VDI palette and a header large enough for one: 1586 bytes of it,
/// most of which the picture does not use. One to eight bitplanes at any size, optionally packed
/// with a dictionary coder — and packed as two halves rather than one stream, so a decoder has to
/// run it twice.
/// </remarks>
public readonly record struct GrafixFile
  : IImageFormatReader<GrafixFile>, IImageToRawImage<GrafixFile>,
    IImageFromRawImage<GrafixFile>, IImageFormatWriter<GrafixFile> {

  /// <summary>The text every file starts with.</summary>
  public const string Signature = "GRXP";

  /// <summary>Offset of the palette.</summary>
  public const int PaletteOffset = 36;

  /// <summary>Offset of the bitmap, or of the first packed half.</summary>
  public const int BitmapOffset = 1586;

  static string IImageFormatMetadata<GrafixFile>.PrimaryExtension => ".grx";
  static string[] IImageFormatMetadata<GrafixFile>.FileExtensions => [".grx"];
  static GrafixFile IImageFormatReader<GrafixFile>.FromSpan(ReadOnlySpan<byte> data)
    => GrafixReader.FromSpan(data);
  static byte[] IImageFormatWriter<GrafixFile>.ToBytes(GrafixFile file) => GrafixWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<GrafixFile>.VideoModes => [
    new("Grafix", [(IntegerRange.Any, IntegerRange.Any)], [new IntegerRange(2, 256)])
  ];

  /// <summary>The unpacked bitmap.</summary>
  public byte[] Bitmap { get; init; }

  /// <summary>The palette as RGB triplets.</summary>
  public byte[] Palette { get; init; }

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>Bitplanes a pixel is spread over.</summary>
  public int Planes { get; init; }

  public static RawImage ToRawImage(GrafixFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = AtariStGraphics.UnpackBitplanes(
      file.Bitmap ?? [], 0, AtariStGraphics.BytesPerRow(file.Width, file.Planes), file.Planes,
      file.Width, file.Height),
    Palette = file.Palette,
    PaletteCount = 1 << file.Planes,
  };

  /// <summary>Writes sixteen colours over four bitplanes, at whatever size the picture is.</summary>
  /// <remarks>
  /// The format takes one, two, four or eight planes. Four is where the trade turns: sixteen chosen
  /// colours cover an ordinary picture far better than four, and 256 costs twice the space for
  /// colours a sixteen-entry palette already approximates once the entries are chosen from the
  /// picture rather than fixed.
  /// </remarks>
  public static GrafixFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    const int planes = 4;
    const int colors = 1 << planes;

    var indexed = image.EnsureIndexedAtMost(colors);
    var palette = new byte[colors * 3];
    (indexed.Palette ?? []).AsSpan(0, Math.Min(colors * 3, indexed.Palette?.Length ?? 0)).CopyTo(palette);

    return new() {
      Width = indexed.Width,
      Height = indexed.Height,
      Planes = planes,
      Palette = palette,
      Bitmap = AtariStGraphics.PackBitplanes(
        indexed.PixelData, AtariStGraphics.BytesPerRow(indexed.Width, planes), planes,
        indexed.Width, indexed.Height),
    };
  }
}
