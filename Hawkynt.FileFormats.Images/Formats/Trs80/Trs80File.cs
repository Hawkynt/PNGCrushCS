using System;
using FileFormat.Core;

namespace FileFormat.Trs80;

/// <summary>In-memory representation of a TRS-80 hi-res graphics screen dump (Model I/III).</summary>
/// <remarks>
/// The Radio Shack hi-res board holds 640 by 240 single-bit pixels, eighty bytes to a row, and the
/// display draws each row twice — which is why a picture drawn on one looks right at 640 by 480 and
/// squashed at its stored height.
/// <para/>
/// What used to be here was a screen of two-by-three block characters at 128 by 48 cells: a shape
/// the machine has no mode for, under the extension that names the hi-res board. Nothing wrote such
/// a file, and none of the ones that exist could be read.
/// </remarks>
public readonly record struct Trs80File : IImageFormatReader<Trs80File>, IImageToRawImage<Trs80File>, IImageFromRawImage<Trs80File>, IImageFormatWriter<Trs80File> {

  /// <summary>Pixels across.</summary>
  internal const int PixelWidth = 640;

  /// <summary>Rows the board stores.</summary>
  internal const int StoredHeight = 240;

  /// <summary>Rows the display shows, each stored one drawn twice.</summary>
  internal const int PixelHeight = StoredHeight * 2;

  /// <summary>Bytes a row takes.</summary>
  internal const int BytesPerRow = PixelWidth / 8;

  /// <summary>Exact bitmap size in bytes.</summary>
  internal const int FileSize = BytesPerRow * StoredHeight;

  static string IImageFormatMetadata<Trs80File>.PrimaryExtension => ".hr";
  static string[] IImageFormatMetadata<Trs80File>.FileExtensions => [".hr"];
  static Trs80File IImageFormatReader<Trs80File>.FromSpan(ReadOnlySpan<byte> data) => Trs80Reader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<Trs80File>.VideoModes => [new("Default", [(640, 480)], [2])];
  static byte[] IImageFormatWriter<Trs80File>.ToBytes(Trs80File file) => Trs80Writer.ToBytes(file);

  /// <summary>Always 640.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 480, the stored rows being doubled.</summary>
  public int Height => PixelHeight;

  /// <summary>The bitmap as stored: 19200 bytes, eighty to a row.</summary>
  public byte[] RawData { get; init; }

  /// <summary>Paper is black here: the display is a phosphor, so a set bit is a lit pixel.</summary>
  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  public static RawImage ToRawImage(Trs80File file) {
    var pixels = new byte[PixelWidth * PixelHeight];

    for (var y = 0; y < StoredHeight; ++y)
    for (var x = 0; x < PixelWidth; ++x) {
      var at = y * BytesPerRow + (x >> 3);
      var lit = at < file.RawData.Length ? (byte)((file.RawData[at] >> (~x & 7)) & 1) : (byte)0;
      pixels[y * 2 * PixelWidth + x] = lit;
      pixels[(y * 2 + 1) * PixelWidth + x] = lit;
    }

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  public static Trs80File FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    if (image.Width != PixelWidth || image.Height != PixelHeight)
      image = image.SampleTo(PixelWidth, PixelHeight);

    // Only every other row is stored; the display puts the missing one back.
    var lit = BilevelRows.Threshold(image, setWhenDark: false);
    var raw = new byte[FileSize];

    for (var y = 0; y < StoredHeight; ++y)
    for (var x = 0; x < PixelWidth; ++x)
      if (lit[y * 2 * PixelWidth + x] != 0)
        raw[y * BytesPerRow + (x >> 3)] |= (byte)(1 << (~x & 7));

    return new() { RawData = raw };
  }
}
