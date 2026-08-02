using System;
using FileFormat.Core;

namespace FileFormat.GigaPaint;

/// <summary>In-memory representation of a GigaPaint hires image.</summary>
public readonly record struct GigaPaintFile : IImageFormatReader<GigaPaintFile>, IImageToRawImage<GigaPaintFile>, IImageFromRawImage<GigaPaintFile>, IImageFormatWriter<GigaPaintFile> {

  static string IImageFormatMetadata<GigaPaintFile>.PrimaryExtension => ".gih";
  static string[] IImageFormatMetadata<GigaPaintFile>.FileExtensions => [".gih", ".gig", ".rpo"];
  static GigaPaintFile IImageFormatReader<GigaPaintFile>.FromSpan(ReadOnlySpan<byte> data) => GigaPaintReader.FromSpan(data);
  static byte[] IImageFormatWriter<GigaPaintFile>.ToBytes(GigaPaintFile file) => GigaPaintWriter.ToBytes(file);

  /// <summary>The fixed width of the image in pixels.</summary>
  public const int FixedWidth = 320;

  /// <summary>The fixed height of the image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>Size of the load address in bytes.</summary>
  internal const int LoadAddressSize = 2;

  /// <summary>Minimum bitmap data size in the payload.</summary>
  internal const int MinBitmapSize = 8000;

  /// <summary>The size of a file: the load address and the bitmap, and nothing else.</summary>
  public const int ExpectedFileSize = LoadAddressSize + MinBitmapSize;

  /// <summary>
  /// The attribute every cell uses. There is no video matrix — the screen is one bit a pixel in the
  /// same two colours throughout, and they are the other way round from most of the machine's
  /// formats: a set bit is black ink, an unset one the white paper under it.
  /// </summary>
  public const byte Attribute = 0x01;

  /// <summary>Image width, always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>C64 memory load address (2 bytes, little-endian).</summary>
  public ushort LoadAddress { get; init; }

  /// <summary>Raw payload data (entire file content after load address).</summary>
  public byte[] RawData { get; init; }

  /// <summary>Bytes a multicolour screen takes: the bitmap, the video matrix, the colour RAM and a background.</summary>
  internal const int MulticolorSize = MinBitmapSize + _SCREEN_SIZE + _SCREEN_SIZE + 1;

  private const int _SCREEN_SIZE = 1000;

  public static RawImage ToRawImage(GigaPaintFile file) {
    var data = file.RawData ?? [];
    var bitmap = new byte[MinBitmapSize];
    data.AsSpan(0, Math.Min(data.Length, MinBitmapSize)).CopyTo(bitmap);

    // Giga Paint draws in two colours or in sixteen, and only the first was read here — so both of
    // the multicolour samples came back in black and white where RECOIL shows nine colours. The two
    // are told apart by length: the multicolour one carries a video matrix, a colour RAM and a
    // background byte after the bitmap, which is the same shape a Koala has.
    if (data.Length >= MulticolorSize) {
      var matrix = data.AsSpan(MinBitmapSize, _SCREEN_SIZE).ToArray();
      var colorRam = data.AsSpan(MinBitmapSize + _SCREEN_SIZE, _SCREEN_SIZE).ToArray();
      var background = data[MinBitmapSize + _SCREEN_SIZE + _SCREEN_SIZE];

      return Commodore64Graphics.DecodeMulticolor(
        bitmap, matrix, colorRam, background, FixedWidth / 2, FixedHeight);
    }

    var screen = new byte[Commodore64Graphics.Columns * (FixedHeight / Commodore64Graphics.CellHeight)];
    Array.Fill(screen, Attribute);

    return Commodore64Graphics.DecodeHires(bitmap, screen, FixedWidth, FixedHeight);
  }

  /// <summary>Builds a screen from a picture, as black ink on white paper.</summary>
  public static GigaPaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    // A set bit is the ink here, so the sample is taken the dark way round.
    var set = GlyphSheet.Sample(image, FixedWidth, FixedHeight, setWhenBright: false);
    var bitmap = new byte[MinBitmapSize];

    for (var y = 0; y < FixedHeight; ++y)
    for (var x = 0; x < FixedWidth; ++x) {
      if (!set[y * FixedWidth + x])
        continue;

      bitmap[(y / 8 * Commodore64Graphics.Columns + x / 8) * 8 + y % 8] |= (byte)(1 << (~x & 7));
    }

    return new() { LoadAddress = 0x2000, RawData = bitmap };
  }

}
