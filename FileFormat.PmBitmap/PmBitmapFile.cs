using System;
using FileFormat.Core;

namespace FileFormat.PmBitmap;

/// <summary>In-memory representation of a PM bitmap image.</summary>
public readonly record struct PmBitmapFile : IImageFormatReader<PmBitmapFile>, IImageToRawImage<PmBitmapFile>, IImageFormatWriter<PmBitmapFile> {

  /// <summary>Header size: 3 magic + 1 version + 2 width + 2 height + 2 depth + 2 padding = 12 bytes.</summary>
  public const int HeaderSize = 12;

  /// <summary>Magic bytes: "PM\0".</summary>
  public static readonly byte[] Magic = [(byte)'P', (byte)'M', 0];

  static string IImageFormatMetadata<PmBitmapFile>.PrimaryExtension => ".pm1";
  static string[] IImageFormatMetadata<PmBitmapFile>.FileExtensions => [".pm1", ".pm2", ".pm3", ".pm4"];
  static PmBitmapFile IImageFormatReader<PmBitmapFile>.FromSpan(ReadOnlySpan<byte> data) => PmBitmapReader.FromSpan(data);
  static byte[] IImageFormatWriter<PmBitmapFile>.ToBytes(PmBitmapFile file) => PmBitmapWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Bits per pixel (8 for grayscale, 24 for RGB).</summary>
  public int Depth { get; init; }

  /// <summary>Format version byte.</summary>
  public byte Version { get; init; }

  /// <summary>Raw pixel data (grayscale or RGB depending on <see cref="Depth"/>).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(PmBitmapFile file) {
    if (file.Depth == 24)
      return new() {
        Width = file.Width,
        Height = file.Height,
        Format = PixelFormat.Rgb24,
        PixelData = file.PixelData[..],
      };

    var pixelCount = file.Width * file.Height;
    var rgb = new byte[pixelCount * 3];
    for (var i = 0; i < pixelCount; ++i) {
      var value = i < file.PixelData.Length ? file.PixelData[i] : (byte)0;
      rgb[i * 3] = value;
      rgb[i * 3 + 1] = value;
      rgb[i * 3 + 2] = value;
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    };
  }

}
