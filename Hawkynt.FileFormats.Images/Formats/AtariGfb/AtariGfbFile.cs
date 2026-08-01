using System;
using FileFormat.Core;

namespace FileFormat.AtariGfb;

/// <summary>In-memory representation of a DeskPic picture (.gfb).</summary>
/// <remarks>
/// Nothing about this format is Atari 8-bit, which is what it was written as here — a bare 320 by
/// 192 monochrome bitmap with no header at all. It is a DeskPic file: a header naming the colours,
/// the size and the length of the bitmap, then interleaved bitplanes in the ST's arrangement, then a
/// palette of 256 entries however few of them the picture uses.
/// <para/>
/// Read as a headerless bitmap, no real file of this format could be opened and no file written
/// could be read by anything else. The name of the type is kept because that is what the extension
/// is registered under.
/// </remarks>
public readonly record struct AtariGfbFile : IImageFormatReader<AtariGfbFile>, IImageToRawImage<AtariGfbFile>, IImageFromRawImage<AtariGfbFile>, IImageFormatWriter<AtariGfbFile> {

  /// <summary>The four characters every one of these begins with.</summary>
  internal const string Signature = "GF25";

  /// <summary>Bytes before the bitmap: the signature and four counts.</summary>
  internal const int HeaderSize = 20;

  /// <summary>Bytes of palette after the bitmap: six a colour, always 256 of them.</summary>
  internal const int PaletteSize = 256 * 6;

  static string IImageFormatMetadata<AtariGfbFile>.PrimaryExtension => ".gfb";
  static string[] IImageFormatMetadata<AtariGfbFile>.FileExtensions => [".gfb"];
  static AtariGfbFile IImageFormatReader<AtariGfbFile>.FromSpan(ReadOnlySpan<byte> data) => AtariGfbReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<AtariGfbFile>.VideoModes => [
    new("Default", [(new IntegerRange(16, 1280, 16), new IntegerRange(1, 1024))], [2, 4, 16, 256])
  ];
  static byte[] IImageFormatWriter<AtariGfbFile>.ToBytes(AtariGfbFile file) => AtariGfbWriter.ToBytes(file);

  /// <summary>Pixels across.</summary>
  public int Width { get; init; }

  /// <summary>Rows.</summary>
  public int Height { get; init; }

  /// <summary>Bitplanes a pixel is built from: one, two, four or eight.</summary>
  public int Bitplanes { get; init; }

  /// <summary>One index a pixel.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>The colours, three bytes each.</summary>
  public byte[] Palette { get; init; }

  /// <summary>Bytes one row of bitplanes takes, which is padded out to whole words.</summary>
  internal static int Stride(int width, int bitplanes) => ((width + 15) >> 4 << 1) * bitplanes;

  public static RawImage ToRawImage(AtariGfbFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Indexed8,
    PixelData = file.PixelData[..],
    Palette = file.Palette[..],
    PaletteCount = 1 << file.Bitplanes,
  };

  public static AtariGfbFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // Eight planes is what the format allows at most, and the reduction is shared rather than each
    // writer's own opinion of what a picture should lose.
    var indexed = image.EnsureIndexedAtMost(256);
    var bitplanes = indexed.PaletteCount switch {
      <= 2 => 1,
      <= 4 => 2,
      <= 16 => 4,
      _ => 8,
    };

    return new() {
      Width = indexed.Width,
      Height = indexed.Height,
      Bitplanes = bitplanes,
      PixelData = indexed.PixelData[..],
      Palette = indexed.Palette ?? new byte[3 << bitplanes],
    };
  }
}
