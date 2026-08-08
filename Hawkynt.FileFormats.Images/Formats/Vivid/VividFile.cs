using System;
using FileFormat.Core;

namespace FileFormat.Vivid;

/// <summary>In-memory representation of a QRT / Vivid ray tracer output (.dis).</summary>
/// <remarks>
/// This used to take bytes 0 to 5 as a size and substitute a default when they looked wrong, and drew
/// whatever followed as interleaved red, green and blue. The size happened to come out right on one
/// sample, because the first four bytes really are the size — but the picture did not, agreeing on
/// four pixels in ten thousand.
/// <para/>
/// A row is not interleaved. After the four-byte size each row carries its own number and then all of
/// its red, all of its green and all of its blue in turn. The arithmetic settles the framing: four
/// bytes plus two hundred rows of two plus 320 times three is 192404, which is one sample to the byte,
/// and the same sum gives 30204 for the other. Read a row at a time in that order both agree with
/// XnView on every pixel.
/// </remarks>
public readonly record struct VividFile
  : IImageFormatReader<VividFile>, IImageToRawImage<VividFile>,
    IImageFromRawImage<VividFile>, IImageFormatWriter<VividFile> {

  static string IImageFormatMetadata<VividFile>.PrimaryExtension => ".vivid";
  static string[] IImageFormatMetadata<VividFile>.FileExtensions => [".vivid", ".dis"];
  static VividFile IImageFormatReader<VividFile>.FromSpan(ReadOnlySpan<byte> data) => VividReader.FromSpan(data);
  static byte[] IImageFormatWriter<VividFile>.ToBytes(VividFile file) => VividWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<VividFile>.VideoModes => [
    new("Default", [(IntegerRange.Any, IntegerRange.Any)], [16777216])
  ];

  /// <summary>Bytes ahead of the first row: the width and the height.</summary>
  internal const int HeaderSize = 4;

  /// <summary>Bytes each row spends on its own number before its colours.</summary>
  internal const int RowNumberSize = 2;

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Three bytes a pixel, red, green and blue.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(VividFile file) => new() {
    Width = file.Width,
    Height = file.Height,
    Format = PixelFormat.Rgb24,
    PixelData = file.PixelData[..],
  };

  public static VividFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Rgb24);

    return new() { Width = image.Width, Height = image.Height, PixelData = image.PixelData[..] };
  }
}
