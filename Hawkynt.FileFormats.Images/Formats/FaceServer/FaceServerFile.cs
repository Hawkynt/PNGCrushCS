using System;
using FileFormat.Core;

namespace FileFormat.FaceServer;

/// <summary>In-memory representation of a FaceServer image (fixed 48x48 grayscale).</summary>
public readonly record struct FaceServerFile : IImageFormatReader<FaceServerFile>, IImageToRawImage<FaceServerFile>, IImageFormatWriter<FaceServerFile> {

  /// <summary>Fixed image width.</summary>
  public const int FixedWidth = 48;

  /// <summary>Fixed image height.</summary>
  public const int FixedHeight = 48;

  /// <summary>Pixel count (48 * 48 = 2304).</summary>
  public const int PixelCount = FixedWidth * FixedHeight;

  static string IImageFormatMetadata<FaceServerFile>.PrimaryExtension => ".fac";
  static string[] IImageFormatMetadata<FaceServerFile>.FileExtensions => [".fac", ".face"];
  static FaceServerFile IImageFormatReader<FaceServerFile>.FromSpan(ReadOnlySpan<byte> data) => FaceServerReader.FromSpan(data);
  static byte[] IImageFormatWriter<FaceServerFile>.ToBytes(FaceServerFile file) => FaceServerWriter.ToBytes(file);

  /// <summary>Always 48.</summary>
  public int Width => FixedWidth;

  /// <summary>Always 48.</summary>
  public int Height => FixedHeight;

  /// <summary>48x48 = 2304 bytes of 8-bit grayscale pixel data.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(FaceServerFile file) {
    var rgb = new byte[PixelCount * 3];
    for (var i = 0; i < PixelCount; ++i) {
      var value = i < file.PixelData.Length ? file.PixelData[i] : (byte)0;
      rgb[i * 3] = value;
      rgb[i * 3 + 1] = value;
      rgb[i * 3 + 2] = value;
    }

    return new() {
      Width = FixedWidth,
      Height = FixedHeight,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

}
