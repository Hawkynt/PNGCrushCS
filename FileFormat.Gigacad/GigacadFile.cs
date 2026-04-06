using System;
using FileFormat.Core;

namespace FileFormat.Gigacad;

/// <summary>In-memory representation of an Atari ST GigaCAD monochrome image (640x400, 1 bitplane).</summary>
public readonly record struct GigacadFile : IImageFormatReader<GigacadFile>, IImageToRawImage<GigacadFile>, IImageFormatWriter<GigacadFile> {

  /// <summary>The exact file size: 80 bytes/line x 400 lines = 32000 bytes.</summary>
  public const int ExpectedFileSize = 32000;

  static string IImageFormatMetadata<GigacadFile>.PrimaryExtension => ".gcd";
  static string[] IImageFormatMetadata<GigacadFile>.FileExtensions => [".gcd"];
  static GigacadFile IImageFormatReader<GigacadFile>.FromSpan(ReadOnlySpan<byte> data) => GigacadReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<GigacadFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<GigacadFile>.ToBytes(GigacadFile file) => GigacadWriter.ToBytes(file);

  /// <summary>Always 640.</summary>
  public int Width => 640;

  /// <summary>Always 400.</summary>
  public int Height => 400;

  /// <summary>Raw monochrome bitmap data (1 bit per pixel, 32000 bytes total).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(GigacadFile file) {

    const int width = 640;
    const int height = 400;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var byteIndex = y * 80 + x / 8;
        var bitIndex = 7 - (x % 8);
        var isSet = byteIndex < file.PixelData.Length && (file.PixelData[byteIndex] & (1 << bitIndex)) != 0;
        // Atari convention: bit=1 is black (0), bit=0 is white (255)
        var color = isSet ? (byte)0 : (byte)255;
        var offset = (y * width + x) * 3;
        rgb[offset] = color;
        rgb[offset + 1] = color;
        rgb[offset + 2] = color;
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

}
