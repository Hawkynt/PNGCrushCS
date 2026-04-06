using System;
using FileFormat.Core;

namespace FileFormat.SbigCcd;

/// <summary>In-memory representation of an SBIG CCD camera image (16-bit grayscale).</summary>
public readonly record struct SbigCcdFile : IImageFormatReader<SbigCcdFile>, IImageToRawImage<SbigCcdFile>, IImageFormatWriter<SbigCcdFile> {

  /// <summary>Header size: 2 width + 2 height + 8 reserved = 12 bytes.</summary>
  public const int HeaderSize = 12;

  static string IImageFormatMetadata<SbigCcdFile>.PrimaryExtension => ".st4";
  static string[] IImageFormatMetadata<SbigCcdFile>.FileExtensions => [".st4", ".stx", ".st5", ".st6", ".st7", ".st8"];
  static SbigCcdFile IImageFormatReader<SbigCcdFile>.FromSpan(ReadOnlySpan<byte> data) => SbigCcdReader.FromSpan(data);
  static byte[] IImageFormatWriter<SbigCcdFile>.ToBytes(SbigCcdFile file) => SbigCcdWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw 16-bit LE grayscale pixel data (2 bytes per pixel).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(SbigCcdFile file) {
    var pixelCount = file.Width * file.Height;
    var rgb = new byte[pixelCount * 3];
    for (var i = 0; i < pixelCount; ++i) {
      var hi = (i * 2 + 1) < file.PixelData.Length ? file.PixelData[i * 2 + 1] : (byte)0;
      rgb[i * 3] = hi;
      rgb[i * 3 + 1] = hi;
      rgb[i * 3 + 2] = hi;
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

}
