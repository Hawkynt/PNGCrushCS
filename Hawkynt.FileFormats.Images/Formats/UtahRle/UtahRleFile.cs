using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.UtahRle;

/// <summary>In-memory representation of a Utah RLE image.</summary>
[FormatMagicBytes([0xCC, 0x52])]
public readonly record struct UtahRleFile : IImageFormatReader<UtahRleFile>, IImageToRawImage<UtahRleFile>, IImageFromRawImage<UtahRleFile>, IImageFormatWriter<UtahRleFile> {

  static string IImageFormatMetadata<UtahRleFile>.PrimaryExtension => ".rle";
  static string[] IImageFormatMetadata<UtahRleFile>.FileExtensions => [".rle", ".urt"];
  static UtahRleFile IImageFormatReader<UtahRleFile>.FromSpan(ReadOnlySpan<byte> data) => UtahRleReader.FromSpan(data);
  static byte[] IImageFormatWriter<UtahRleFile>.ToBytes(UtahRleFile file) => UtahRleWriter.ToBytes(file);

  public int XPos { get; init; }
  public int YPos { get; init; }
  public int Width { get; init; }
  public int Height { get; init; }
  public int NumChannels { get; init; }
  public byte[] PixelData { get; init; }
  public byte[]? BackgroundColor { get; init; }

  /// <summary>Converts this Utah RLE image to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(UtahRleFile file) {

    var width = file.Width;
    var height = file.Height;

    return file.NumChannels switch {
      1 => new() {
        Width = width,
        Height = height,
        Format = PixelFormat.Gray8,
        PixelData = _CopyPixelData(file.PixelData, width * height),
      },
      3 => new() {
        Width = width,
        Height = height,
        Format = PixelFormat.Rgb24,
        PixelData = _CopyPixelData(file.PixelData, width * height * 3),
      },
      4 => new() {
        Width = width,
        Height = height,
        Format = PixelFormat.Rgba32,
        PixelData = _CopyPixelData(file.PixelData, width * height * 4),
      },
      _ => throw new InvalidDataException($"Unsupported channel count for Utah RLE: {file.NumChannels}. Expected 1, 3, or 4."),
    };
  }

  /// <summary>Creates a Utah RLE file from a platform-independent <see cref="RawImage"/>.</summary>
  public static UtahRleFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var channels = image.Format switch {
      PixelFormat.Gray8 => 1,
      PixelFormat.Rgb24 => 3,
      PixelFormat.Rgba32 => 4,
      _ => throw new ArgumentException($"Unsupported pixel format for Utah RLE: {image.Format}. Expected Gray8, Rgb24, or Rgba32.", nameof(image)),
    };

    return new() {
      Width = image.Width,
      Height = image.Height,
      NumChannels = channels,
      PixelData = image.PixelData[..],
    };
  }

  private static byte[] _CopyPixelData(byte[] source, int expectedLength) {
    var result = new byte[expectedLength];
    source.AsSpan(0, Math.Min(source.Length, expectedLength)).CopyTo(result);
    return result;
  }
}
