using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.SbigCcd;

/// <summary>In-memory representation of an SBIG CCD camera image (16-bit grayscale).</summary>
public sealed class SbigCcdFile : IImageFileFormat<SbigCcdFile> {

  /// <summary>Header size: 2 width + 2 height + 8 reserved = 12 bytes.</summary>
  public const int HeaderSize = 12;

  static string IImageFileFormat<SbigCcdFile>.PrimaryExtension => ".st4";
  static string[] IImageFileFormat<SbigCcdFile>.FileExtensions => [".st4", ".stx", ".st5", ".st6", ".st7", ".st8"];
  static SbigCcdFile IImageFileFormat<SbigCcdFile>.FromFile(FileInfo file) => SbigCcdReader.FromFile(file);
  static SbigCcdFile IImageFileFormat<SbigCcdFile>.FromBytes(byte[] data) => SbigCcdReader.FromBytes(data);
  static SbigCcdFile IImageFileFormat<SbigCcdFile>.FromStream(Stream stream) => SbigCcdReader.FromStream(stream);
  static RawImage IImageFileFormat<SbigCcdFile>.ToRawImage(SbigCcdFile file) => ToRawImage(file);
  static byte[] IImageFileFormat<SbigCcdFile>.ToBytes(SbigCcdFile file) => SbigCcdWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Raw 16-bit LE grayscale pixel data (2 bytes per pixel).</summary>
  public byte[] PixelData { get; init; } = [];

  public static RawImage ToRawImage(SbigCcdFile file) {
    ArgumentNullException.ThrowIfNull(file);
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

  public static SbigCcdFile FromRawImage(RawImage image) => throw new NotSupportedException("SBIG CCD writing from raw image is not supported.");
}
