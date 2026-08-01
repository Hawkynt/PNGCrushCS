using System;
using FileFormat.Core;

namespace FileFormat.AtariHr;

/// <summary>In-memory representation of an Atari 8-bit HR screen dump.</summary>
/// <remarks>
/// Two Graphics 8 screens shown on alternate television fields. Each is one bit a pixel and can
/// only be black or white, but a pixel set in one field and clear in the other reads as grey — so
/// the pair carries three levels where either alone carries two. That is the whole point of the
/// format, and it is why the file is two 8192-byte halves rather than one screen.
/// <para/>
/// It used to be read as a single 320-by-192 screen of 7680 bytes: half the size, the wrong shape,
/// and without the second field there are no greys at all.
/// </remarks>
public readonly record struct AtariHrFile : IImageFormatReader<AtariHrFile>, IImageToRawImage<AtariHrFile>, IImageFromRawImage<AtariHrFile>, IImageFormatWriter<AtariHrFile> {

  /// <summary>Fixed width in pixels.</summary>
  internal const int PixelWidth = 256;

  /// <summary>Fixed height in pixels.</summary>
  internal const int PixelHeight = 239;

  /// <summary>Bytes per scanline row (256 / 8 = 32).</summary>
  internal const int BytesPerRow = PixelWidth / 8;

  /// <summary>Where the second field starts.</summary>
  internal const int SecondFieldOffset = 8192;

  /// <summary>Exact file size in bytes: two fields of 8192.</summary>
  internal const int FileSize = SecondFieldOffset * 2;

  /// <summary>The colour register a set bit draws from; a clear one draws black.</summary>
  private const int LitRegister = 14;

  static string IImageFormatMetadata<AtariHrFile>.PrimaryExtension => ".hr";
  static string[] IImageFormatMetadata<AtariHrFile>.FileExtensions => [".hr"];
  static AtariHrFile IImageFormatReader<AtariHrFile>.FromSpan(ReadOnlySpan<byte> data) => AtariHrReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<AtariHrFile>.VideoModes => [
    new("Default", [(PixelWidth, PixelHeight)], [3])
  ];
  static byte[] IImageFormatWriter<AtariHrFile>.ToBytes(AtariHrFile file) => AtariHrWriter.ToBytes(file);

  /// <summary>Always 256.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 239.</summary>
  public int Height => PixelHeight;

  /// <summary>Both fields as stored: 16384 bytes, the second starting at 8192.</summary>
  public byte[] RawData { get; init; }

  /// <summary>Renders the two fields as the display blends them: black, grey, white.</summary>
  public static RawImage ToRawImage(AtariHrFile file) {
    var first = new byte[PixelWidth * PixelHeight];
    var second = new byte[PixelWidth * PixelHeight];

    for (var y = 0; y < PixelHeight; ++y)
    for (var x = 0; x < PixelWidth; ++x) {
      var at = y * BytesPerRow + (x >> 3);
      first[y * PixelWidth + x] = _Register(file.RawData, at, x);
      second[y * PixelWidth + x] = _Register(file.RawData, SecondFieldOffset + at, x);
    }

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Rgb24,
      PixelData = Atari8BitGraphics.BlendFrames(
        Atari8BitGraphics.ApplyPalette(first), Atari8BitGraphics.ApplyPalette(second)),
    };
  }

  private static byte _Register(byte[] data, int at, int x)
    => at < data.Length && ((data[at] >> (~x & 7)) & 1) != 0 ? (byte)LitRegister : (byte)0;

  /// <summary>Splits a picture into the two fields whose average it is.</summary>
  /// <remarks>
  /// Three levels come out of two bits: neither set is black, both set is white, and one of each is
  /// the grey between them. Which field gets the lone bit does not matter to the display, so it
  /// always goes to the first.
  /// </remarks>
  public static AtariHrFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var gray = PixelConverter.Convert(image.SampleTo(PixelWidth, PixelHeight), PixelFormat.Gray8);
    var raw = new byte[FileSize];

    // Where the three levels sit: black, the lit register, and the average of the two.
    var palette = Atari8BitGraphics.CreatePalette();
    int dark = palette[1], light = palette[LitRegister * 3 + 1];
    var middle = (dark + light) >> 1;

    for (var y = 0; y < PixelHeight; ++y)
    for (var x = 0; x < PixelWidth; ++x) {
      var value = gray.PixelData[y * PixelWidth + x];
      var at = y * BytesPerRow + (x >> 3);
      var bit = (byte)(1 << (~x & 7));

      if (value >= (middle + light) / 2) {
        raw[at] |= bit;
        raw[SecondFieldOffset + at] |= bit;
      } else if (value >= (dark + middle) / 2)
        raw[at] |= bit;
    }

    return new() { RawData = raw };
  }
}
