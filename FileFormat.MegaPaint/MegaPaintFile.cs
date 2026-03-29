using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MegaPaint;

/// <summary>In-memory representation of an Atari ST MegaPaint monochrome image.</summary>
public sealed class MegaPaintFile : IImageFileFormat<MegaPaintFile> {

  /// <summary>Header size in bytes: 2 (width) + 2 (height) + 4 (reserved) = 8.</summary>
  public const int HeaderSize = 8;

  /// <summary>Minimum file size for validation.</summary>
  public const int MinFileSize = 8;

  static string IImageFileFormat<MegaPaintFile>.PrimaryExtension => ".bld";
  static string[] IImageFileFormat<MegaPaintFile>.FileExtensions => [".bld"];
  static FormatCapability IImageFileFormat<MegaPaintFile>.Capabilities => FormatCapability.MonochromeOnly;
  static MegaPaintFile IImageFileFormat<MegaPaintFile>.FromFile(FileInfo file) => MegaPaintReader.FromFile(file);
  static MegaPaintFile IImageFileFormat<MegaPaintFile>.FromBytes(byte[] data) => MegaPaintReader.FromBytes(data);
  static MegaPaintFile IImageFileFormat<MegaPaintFile>.FromStream(Stream stream) => MegaPaintReader.FromStream(stream);
  static byte[] IImageFileFormat<MegaPaintFile>.ToBytes(MegaPaintFile file) => MegaPaintWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Raw monochrome bitmap data (1 bit per pixel, padded to byte boundary per row).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(MegaPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var width = file.Width;
    var height = file.Height;
    var bytesPerRow = (width + 7) / 8;
    var rgb = new byte[width * height * 3];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var byteIndex = y * bytesPerRow + x / 8;
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

  public static MegaPaintFile FromRawImage(RawImage image) => throw new NotSupportedException("MegaPaint format does not support creation from RawImage.");
}
