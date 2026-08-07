using System;
using FileFormat.Core;

namespace FileFormat.IndyPaint;

/// <summary>In-memory representation of an IndyPaint (.ipn) screen dump.</summary>
public readonly record struct IndyPaintFile : IImageFormatReader<IndyPaintFile>, IImageToRawImage<IndyPaintFile>, IImageFromRawImage<IndyPaintFile>, IImageFormatWriter<IndyPaintFile> {

  /// <summary>The exact file size: 320 x 240 x 2 bytes per pixel.</summary>
  /// <summary>ASCII signature every IndyPaint file starts with.</summary>
  public static ReadOnlySpan<byte> Signature => "Indy"u8;

  /// <summary>Offset of the big-endian width/height pair.</summary>
  public const int DimensionsOffset = 4;

  /// <summary>Header size; pixel data starts here.</summary>
  public const int HeaderSize = 256;

  /// <summary>Bytes a pixel: big-endian RGB565.</summary>
  public const int BytesPerPixel = 2;

  /// <summary>The size the commonest one has, which is not the only one.</summary>
  public const int DefaultWidth = 320, DefaultHeight = 240;

  /// <summary>Pixel data size at the default width.</summary>
  public const int PixelDataSize = DefaultWidth * DefaultHeight * BytesPerPixel;

  /// <summary>The file size at the default width.</summary>
  public const int ExpectedFileSize = HeaderSize + PixelDataSize;

  static string IImageFormatMetadata<IndyPaintFile>.PrimaryExtension => ".ipn";
  static string[] IImageFormatMetadata<IndyPaintFile>.FileExtensions => [".ipn", ".idy", ".tru"];
  static IndyPaintFile IImageFormatReader<IndyPaintFile>.FromSpan(ReadOnlySpan<byte> data) => IndyPaintReader.FromSpan(data);

  /// <summary>
  /// The header states the size, so more than one is held.
  /// </summary>
  /// <remarks>
  /// This declared 320 by 240 as the only one and the reader took that length and no other, so a
  /// 384-wide picture — which the samples have as readily as 320 — was refused for being the size
  /// it says it is.
  /// </remarks>
  static VideoMode[] IImageFormatMetadata<IndyPaintFile>.VideoModes => [
    new("Default", [(DefaultWidth, DefaultHeight), (384, DefaultHeight)]),
  ];
  static byte[] IImageFormatWriter<IndyPaintFile>.ToBytes(IndyPaintFile file) => IndyPaintWriter.ToBytes(file);

  /// <summary>Pixels across, as the header states.</summary>
  public int Width { get; init; }

  /// <summary>Pixels down, as the header states.</summary>
  public int Height { get; init; }

  /// <summary>Raw RGB565 big-endian pixel data (2 bytes per pixel, 153600 bytes total).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(IndyPaintFile file) {

    var rgb565 = file.PixelData;
    var pixelCount = file.Width * file.Height;
    var rgb24 = new byte[pixelCount * 3];

    for (var i = 0; i < pixelCount; ++i) {
      var srcOffset = i * 2;
      var hi = srcOffset < rgb565.Length ? rgb565[srcOffset] : (byte)0;
      var lo = srcOffset + 1 < rgb565.Length ? rgb565[srcOffset + 1] : (byte)0;
      var packed = (ushort)((hi << 8) | lo);

      var r5 = (packed >> 11) & 0x1F;
      var g6 = (packed >> 5) & 0x3F;
      var b5 = packed & 0x1F;

      var dstOffset = i * 3;
      rgb24[dstOffset] = (byte)((r5 << 3) | (r5 >> 2));
      rgb24[dstOffset + 1] = (byte)((g6 << 2) | (g6 >> 4));
      rgb24[dstOffset + 2] = (byte)((b5 << 3) | (b5 >> 2));
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb24,
    };
  }

  public static IndyPaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Rgb24);
    if (image.Width < 1 || image.Height < 1)
      throw new ArgumentException($"A picture needs at least one pixel; got {image.Width}x{image.Height}.", nameof(image));

    var rgb24 = image.PixelData;
    var pixelCount = image.Width * image.Height;
    var rgb565 = new byte[pixelCount * 2];

    for (var i = 0; i < pixelCount; ++i) {
      var srcOffset = i * 3;
      var r = rgb24[srcOffset];
      var g = rgb24[srcOffset + 1];
      var b = rgb24[srcOffset + 2];

      var r5 = (r >> 3) & 0x1F;
      var g6 = (g >> 2) & 0x3F;
      var b5 = (b >> 3) & 0x1F;
      var packed = (ushort)((r5 << 11) | (g6 << 5) | b5);

      var dstOffset = i * 2;
      rgb565[dstOffset] = (byte)(packed >> 8);
      rgb565[dstOffset + 1] = (byte)(packed & 0xFF);

      // Quantize input in-place to the lossy RGB565 range so the round-trip is bit-exact.
      rgb24[srcOffset] = (byte)((r5 << 3) | (r5 >> 2));
      rgb24[srcOffset + 1] = (byte)((g6 << 2) | (g6 >> 4));
      rgb24[srcOffset + 2] = (byte)((b5 << 3) | (b5 >> 2));
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = rgb565,
    };
  }
}
