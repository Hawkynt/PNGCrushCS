using System;
using FileFormat.Core;

namespace FileFormat.DoodleAtari;

/// <summary>In-memory representation of an Atari ST Doodle monochrome image (640x400, 1 bitplane).</summary>
public readonly record struct DoodleAtariFile : IImageFormatReader<DoodleAtariFile>, IImageToRawImage<DoodleAtariFile>, IImageFormatWriter<DoodleAtariFile> {

  /// <summary>The exact file size: 80 bytes/line x 400 lines = 32000 bytes.</summary>
  public const int ExpectedFileSize = 32000;

  static string IImageFormatMetadata<DoodleAtariFile>.PrimaryExtension => ".doo";
  static string[] IImageFormatMetadata<DoodleAtariFile>.FileExtensions => [".doo"];
  static DoodleAtariFile IImageFormatReader<DoodleAtariFile>.FromSpan(ReadOnlySpan<byte> data) => DoodleAtariReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<DoodleAtariFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<DoodleAtariFile>.ToBytes(DoodleAtariFile file) => DoodleAtariWriter.ToBytes(file);

  /// <summary>Always 640.</summary>
  public int Width => 640;

  /// <summary>Always 400.</summary>
  public int Height => 400;

  /// <summary>Raw monochrome bitmap data (1 bit per pixel, 32000 bytes total).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(DoodleAtariFile file) {

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
