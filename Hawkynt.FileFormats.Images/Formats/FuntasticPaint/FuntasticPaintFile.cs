using System;
using FileFormat.Core;

namespace FileFormat.FuntasticPaint;

/// <summary>In-memory representation of a Fun*tastic Paint (Atari 8-bit GTIA 16-shade) image (80x192).</summary>
public readonly record struct FuntasticPaintFile : IImageFormatReader<FuntasticPaintFile>, IImageToRawImage<FuntasticPaintFile>, IImageFromRawImage<FuntasticPaintFile>, IImageFormatWriter<FuntasticPaintFile> {

  /// <summary>The exact file size: 40 bytes/line x 192 lines.</summary>
  public const int ExpectedFileSize = 7680;

  /// <summary>The fixed width in pixels.</summary>
  public const int FixedWidth = 80;

  /// <summary>The fixed height in pixels.</summary>
  public const int FixedHeight = 192;

  /// <summary>Bytes per scanline (2 pixels per byte, 80/2 = 40).</summary>
  internal const int BytesPerRow = 40;

  static string IImageFormatMetadata<FuntasticPaintFile>.PrimaryExtension => ".fun8";
  static string[] IImageFormatMetadata<FuntasticPaintFile>.FileExtensions => [".fun8", ".ftp"];
  static FuntasticPaintFile IImageFormatReader<FuntasticPaintFile>.FromSpan(ReadOnlySpan<byte> data) => FuntasticPaintReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<FuntasticPaintFile>.VideoModes => [
    new("Default", [(80, 192)], [new IntegerRange(2, 16)])
  ];
  static byte[] IImageFormatWriter<FuntasticPaintFile>.ToBytes(FuntasticPaintFile file) => FuntasticPaintWriter.ToBytes(file);

  /// <summary>Always 80.</summary>
  public int Width => FixedWidth;

  /// <summary>Always 192.</summary>
  public int Height => FixedHeight;

  /// <summary>Raw pixel data (7680 bytes, 2 pixels per byte as high/low nybbles, 40 bytes per row, 192 rows).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Converts this image to a platform-independent <see cref="RawImage"/> in Gray8 format.</summary>
  public static RawImage ToRawImage(FuntasticPaintFile file) {

    var gray = new byte[FixedWidth * FixedHeight];

    for (var y = 0; y < FixedHeight; ++y)
      for (var x = 0; x < FixedWidth; ++x) {
        var byteIndex = y * BytesPerRow + x / 2;
        int shade;
        if ((x & 1) == 0)
          shade = (file.PixelData[byteIndex] >> 4) & 0x0F;
        else
          shade = file.PixelData[byteIndex] & 0x0F;

        gray[y * FixedWidth + x] = (byte)(shade * 17);
      }

    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Gray8,
      PixelData = gray,
    };
  }

  /// <summary>Creates a Fun*tastic Paint file from a platform-independent <see cref="RawImage"/>. Accepts Gray8, Indexed1, or Indexed8.</summary>
  public static FuntasticPaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    // Converted rather than refused. Every other writer here takes whatever picture it is
    // handed and does what the format needs; one that insists the caller reduce first pushes
    // that work onto anything converting between formats, which is most of what this is for.
    image = image.EnsureAnyFormat(PixelFormat.Gray8, PixelFormat.Indexed8, PixelFormat.Indexed1);
    if (image.Width != FixedWidth || image.Height != FixedHeight)
      throw new ArgumentException($"Expected {FixedWidth}x{FixedHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var pixelData = new byte[ExpectedFileSize];

    if (image.Format == PixelFormat.Indexed1) {
      var stride = (FixedWidth + 7) / 8;
      for (var y = 0; y < FixedHeight; ++y)
        for (var x = 0; x < FixedWidth; ++x) {
          var b = image.PixelData[y * stride + (x >> 3)];
          var shade = (b >> (7 - (x & 7))) & 1;
          var byteIndex = y * BytesPerRow + x / 2;
          if ((x & 1) == 0)
            pixelData[byteIndex] |= (byte)(shade << 4);
          else
            pixelData[byteIndex] |= (byte)shade;
        }
    } else if (image.Format == PixelFormat.Indexed8) {
      for (var y = 0; y < FixedHeight; ++y)
        for (var x = 0; x < FixedWidth; ++x) {
          var shade = image.PixelData[y * FixedWidth + x] & 0x0F;
          var byteIndex = y * BytesPerRow + x / 2;
          if ((x & 1) == 0)
            pixelData[byteIndex] |= (byte)(shade << 4);
          else
            pixelData[byteIndex] |= (byte)shade;
        }
    } else if (image.Format == PixelFormat.Gray8) {
      for (var y = 0; y < FixedHeight; ++y)
        for (var x = 0; x < FixedWidth; ++x) {
          var shade = image.PixelData[y * FixedWidth + x] / 17;
          if (shade > 15)
            shade = 15;

          var byteIndex = y * BytesPerRow + x / 2;
          if ((x & 1) == 0)
            pixelData[byteIndex] |= (byte)(shade << 4);
          else
            pixelData[byteIndex] |= (byte)shade;
        }
    } else {
      throw new ArgumentException($"Expected {PixelFormat.Gray8}, {PixelFormat.Indexed1}, or {PixelFormat.Indexed8} but got {image.Format}.", nameof(image));
    }

    return new() { PixelData = pixelData };
  }
}
