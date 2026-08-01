using System;
using FileFormat.Core;

namespace FileFormat.CoCoMax;

/// <summary>In-memory representation of a CoCoMax picture (.max, .p41).</summary>
/// <remarks>
/// A Colour Computer screen, monochrome despite the machine's name: 256 by 192 at one bit a pixel.
/// The picture does not start at the head of the file — five bytes of header come first, and four
/// lengths are legal, the bytes past the picture being whatever the program's buffer happened to
/// hold.
/// </remarks>
public readonly record struct CoCoMaxFile : IImageFormatReader<CoCoMaxFile>, IImageToRawImage<CoCoMaxFile>, IImageFromRawImage<CoCoMaxFile>, IImageFormatWriter<CoCoMaxFile> {

  static string IImageFormatMetadata<CoCoMaxFile>.PrimaryExtension => ".max";
  static string[] IImageFormatMetadata<CoCoMaxFile>.FileExtensions => [".max", ".p41"];
  static CoCoMaxFile IImageFormatReader<CoCoMaxFile>.FromSpan(ReadOnlySpan<byte> data) => CoCoMaxReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<CoCoMaxFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])
  ];
  static byte[] IImageFormatWriter<CoCoMaxFile>.ToBytes(CoCoMaxFile file) => CoCoMaxWriter.ToBytes(file);

  /// <summary>Offset of the bitmap, after the header the program wrote.</summary>
  public const int BitmapOffset = 5;

  /// <summary>Bytes of bitmap the picture occupies.</summary>
  public const int BitmapSize = BytesPerRow * PixelHeight;

  /// <summary>The size a file written from a picture takes, which is the smallest legal one.</summary>
  public const int ExpectedFileSize = 6154;

  /// <summary>The lengths a file may be; the picture is the same in all of them.</summary>
  public static ReadOnlySpan<int> LegalSizes => [6154, 6155, 6272, 7168];

  /// <summary>Writes the five bytes a reader identifies the format by.</summary>
  public static void WriteHeader(Span<byte> data) {
    data[0] = 0;
    data[1] = 24;
    data[2] = 0;
    data[3] = 14;
    data[4] = 0;
  }

  /// <summary>Image width in pixels.</summary>
  internal const int PixelWidth = 256;

  /// <summary>Image height in pixels.</summary>
  internal const int PixelHeight = 192;

  /// <summary>Bytes per scanline.</summary>
  internal const int BytesPerRow = 32;

  /// <summary>Always 256.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 192.</summary>
  public int Height => PixelHeight;

  /// <summary>The whole file, header and all, because the picture does not begin at its start.</summary>
  public byte[] RawData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts the CoCoMax screen to an Indexed1 raw image (256x192, B&amp;W palette).</summary>
  public static RawImage ToRawImage(CoCoMaxFile file) {

    var data = file.RawData ?? [];
    var pixelData = new byte[BitmapSize];
    var available = Math.Max(0, Math.Min(data.Length - BitmapOffset, BitmapSize));
    if (available > 0)
      data.AsSpan(BitmapOffset, available).CopyTo(pixelData);

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed1,
      PixelData = pixelData,
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  /// <summary>Creates a CoCoMax screen from an Indexed1 raw image (256x192).</summary>
  public static CoCoMaxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Indexed1);
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var rawData = new byte[ExpectedFileSize];
    WriteHeader(rawData);
    image.PixelData.AsSpan(0, Math.Min(image.PixelData.Length, BitmapSize)).CopyTo(rawData.AsSpan(BitmapOffset));

    return new CoCoMaxFile { RawData = rawData };
  }
}
