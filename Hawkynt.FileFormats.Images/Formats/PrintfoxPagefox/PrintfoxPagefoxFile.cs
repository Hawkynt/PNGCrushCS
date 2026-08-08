using System;
using FileFormat.Core;

namespace FileFormat.PrintfoxPagefox;

/// <summary>In-memory representation of a Printfox/Pagefox hires image.</summary>
public readonly record struct PrintfoxPagefoxFile : IImageFormatReader<PrintfoxPagefoxFile>, IImageToRawImage<PrintfoxPagefoxFile>, IImageFromRawImage<PrintfoxPagefoxFile>, IImageFormatWriter<PrintfoxPagefoxFile> {

  static string IImageFormatMetadata<PrintfoxPagefoxFile>.PrimaryExtension => ".bs";
  static string[] IImageFormatMetadata<PrintfoxPagefoxFile>.FileExtensions => [".bs", ".pg"];
  static PrintfoxPagefoxFile IImageFormatReader<PrintfoxPagefoxFile>.FromSpan(ReadOnlySpan<byte> data) => PrintfoxPagefoxReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<PrintfoxPagefoxFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])];
  static byte[] IImageFormatWriter<PrintfoxPagefoxFile>.ToBytes(PrintfoxPagefoxFile file) => PrintfoxPagefoxWriter.ToBytes(file);

  /// <summary>The fixed width of the image in pixels.</summary>
  public const int FixedWidth = 320;

  /// <summary>The fixed height of the image in pixels.</summary>
  public const int FixedHeight = 200;

  /// <summary>Bytes per row (320 / 8 = 40).</summary>
  internal const int BytesPerRow = FixedWidth / 8;

  /// <summary>Minimum raw data size (40 bytes/row * 200 rows = 8000).</summary>
  internal const int MinDataSize = BytesPerRow * FixedHeight;

  /// <summary>Black and white palette (2 entries, 3 bytes each).</summary>
  // Paper first. Both reference tools draw a clear bit white, every pixel of the sample, and two of
  // them agreeing against us is a defect rather than an opinion.
  private static readonly byte[] _BlackWhitePalette = [255, 255, 255, 0, 0, 0];

  /// <summary>Image width, always 320.</summary>
  public int Width => FixedWidth;

  /// <summary>Image height, always 200.</summary>
  public int Height => FixedHeight;

  /// <summary>Raw bitmap data (at least 8000 bytes of 1bpp packed pixel data).</summary>
  public byte[] RawData { get; init; }

  /// <summary>Converts this Printfox/Pagefox image to an Indexed1 <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(PrintfoxPagefoxFile file) {

    var pixelData = new byte[BytesPerRow * FixedHeight];
    var copyLength = Math.Min(file.RawData.Length, pixelData.Length);
    file.RawData.AsSpan(0, copyLength).CopyTo(pixelData.AsSpan(0));

    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Indexed1,
      PixelData = pixelData,
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }


  /// <summary>Encodes a picture as a Printfox page, scaling it to 320x200 first.</summary>
  /// <remarks>
  /// Paper is the clear bit and ink the set one, which is the way round both reference tools draw
  /// it. The picture is held in rows here and only put into character cells by the writer, so this
  /// produces rows — writing cells would agree with nothing but itself.
  /// </remarks>
  public static PrintfoxPagefoxFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgb = image.SampleTo(FixedWidth, FixedHeight).PixelData;
    var rows = new byte[MinDataSize];

    for (var y = 0; y < FixedHeight; ++y)
    for (var x = 0; x < FixedWidth; ++x) {
      var at = (y * FixedWidth + x) * 3;
      var luminance = (rgb[at] * 77 + rgb[at + 1] * 151 + rgb[at + 2] * 28) >> 8;
      if (luminance < 128)
        rows[y * BytesPerRow + x / 8] |= (byte)(0x80 >> (x % 8));
    }

    return new() { RawData = rows };
  }

}
