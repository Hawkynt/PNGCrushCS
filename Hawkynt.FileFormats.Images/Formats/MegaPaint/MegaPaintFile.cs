using System;
using FileFormat.Core;

namespace FileFormat.MegaPaint;

/// <summary>In-memory representation of an Atari ST MegaPaint monochrome image.</summary>
public readonly record struct MegaPaintFile : IImageFormatReader<MegaPaintFile>, IImageToRawImage<MegaPaintFile>, IImageFromRawImage<MegaPaintFile>, IImageFormatWriter<MegaPaintFile> {

  /// <summary>Header size in bytes: 2 (width) + 2 (height) + 4 (reserved) = 8.</summary>
  public const int HeaderSize = 4;

  /// <summary>Minimum file size for validation.</summary>
  public const int MinFileSize = 4;

  static string IImageFormatMetadata<MegaPaintFile>.PrimaryExtension => ".bld";
  static string[] IImageFormatMetadata<MegaPaintFile>.FileExtensions => [".bld"];
  static MegaPaintFile IImageFormatReader<MegaPaintFile>.FromSpan(ReadOnlySpan<byte> data) => MegaPaintReader.FromSpan(data);
  static VideoMode[] IImageFormatMetadata<MegaPaintFile>.VideoModes => [new("Default", [(IntegerRange.Any, IntegerRange.Any)], [2])];
  static byte[] IImageFormatWriter<MegaPaintFile>.ToBytes(MegaPaintFile file) => MegaPaintWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Raw monochrome bitmap data (1 bit per pixel, padded to byte boundary per row).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(MegaPaintFile file) {

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

  /// <summary>Creates a MegaPaint monochrome image from a <see cref="RawImage"/> of any size up to 65536 a side.</summary>
  /// <remarks>
  /// Follows the Atari convention <see cref="ToRawImage"/> decodes: a set bit is black. Sources with
  /// more than two tones are thresholded at mid-grey. The size ceiling is 65536 rather than 65535
  /// because the header states the last column and row, not the counts.
  /// </remarks>
  public static MegaPaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var gray = image.EnsureFormat(PixelFormat.Gray8);
    var bytesPerRow = (gray.Width + 7) / 8;
    var pixels = new byte[bytesPerRow * gray.Height];

    for (var y = 0; y < gray.Height; ++y)
      for (var x = 0; x < gray.Width; ++x) {
        if (gray.PixelData[y * gray.Width + x] >= 128)
          continue;

        pixels[y * bytesPerRow + x / 8] |= (byte)(1 << (7 - (x % 8)));
      }

    return new() {
      Width = gray.Width,
      Height = gray.Height,
      PixelData = pixels,
    };
  }

}
