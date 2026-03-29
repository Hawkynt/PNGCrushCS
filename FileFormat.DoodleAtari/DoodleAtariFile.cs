using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.DoodleAtari;

/// <summary>In-memory representation of an Atari ST Doodle monochrome image (640x400, 1 bitplane).</summary>
public sealed class DoodleAtariFile : IImageFileFormat<DoodleAtariFile> {

  /// <summary>The exact file size: 80 bytes/line x 400 lines = 32000 bytes.</summary>
  public const int ExpectedFileSize = 32000;

  static string IImageFileFormat<DoodleAtariFile>.PrimaryExtension => ".doo";
  static string[] IImageFileFormat<DoodleAtariFile>.FileExtensions => [".doo"];
  static FormatCapability IImageFileFormat<DoodleAtariFile>.Capabilities => FormatCapability.MonochromeOnly;
  static DoodleAtariFile IImageFileFormat<DoodleAtariFile>.FromFile(FileInfo file) => DoodleAtariReader.FromFile(file);
  static DoodleAtariFile IImageFileFormat<DoodleAtariFile>.FromBytes(byte[] data) => DoodleAtariReader.FromBytes(data);
  static DoodleAtariFile IImageFileFormat<DoodleAtariFile>.FromStream(Stream stream) => DoodleAtariReader.FromStream(stream);
  static byte[] IImageFileFormat<DoodleAtariFile>.ToBytes(DoodleAtariFile file) => DoodleAtariWriter.ToBytes(file);

  /// <summary>Always 640.</summary>
  public int Width => 640;

  /// <summary>Always 400.</summary>
  public int Height => 400;

  /// <summary>Raw monochrome bitmap data (1 bit per pixel, 32000 bytes total).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(DoodleAtariFile file) {
    ArgumentNullException.ThrowIfNull(file);

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

  public static DoodleAtariFile FromRawImage(RawImage image) => throw new NotSupportedException("DoodleAtari format does not support creation from RawImage.");
}
