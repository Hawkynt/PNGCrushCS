using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.IffDpan;

/// <summary>In-memory representation of a Deluxe Paint DPAN animation, exposing its first frame as an image.</summary>
public readonly record struct IffDpanFile : IImageFormatReader<IffDpanFile>, IImageToRawImage<IffDpanFile>, IImageFromRawImage<IffDpanFile>, IImageFormatWriter<IffDpanFile> {

  /// <summary>Minimum IFF container header size: <c>FORM</c>, a 32-bit size and the form type.</summary>
  internal const int MinFileSize = 12;

  /// <summary>The DPAN chunk version documented for released Deluxe Paint animations.</summary>
  internal const ushort CurrentVersion = 4;

  static string IImageFormatMetadata<IffDpanFile>.PrimaryExtension => ".dpan";
  static string[] IImageFormatMetadata<IffDpanFile>.FileExtensions => [".dpan"];
  static IffDpanFile IImageFormatReader<IffDpanFile>.FromSpan(ReadOnlySpan<byte> data) => IffDpanReader.FromSpan(data);
  static byte[] IImageFormatWriter<IffDpanFile>.ToBytes(IffDpanFile file) => IffDpanWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>RGB24 pixels decoded from the first ILBM frame.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Original file bytes when this instance came from a reader; empty for newly authored images.</summary>
  public byte[] RawData { get; init; }

  /// <summary>DPAN chunk version.</summary>
  public ushort Version { get; init; }

  /// <summary>Number of animation frames declared by the DPAN chunk.</summary>
  public ushort FrameCount { get; init; }

  /// <summary>Reserved DPAN flags field.</summary>
  public uint Flags { get; init; }

  /// <summary>Converts the first animation frame to a platform-independent <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(IffDpanFile file) {
    var expectedLength = checked(file.Width * file.Height * 3);
    if (file.PixelData is not { } pixels || pixels.Length != expectedLength)
      throw new InvalidDataException($"DPAN RGB24 pixel data must contain exactly {expectedLength} bytes.");

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels[..],
    };
  }

  /// <summary>Creates a single-frame Deluxe Paint animation from a platform-independent image.</summary>
  public static IffDpanFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var pixels = image.Format == PixelFormat.Rgb24
      ? image.PixelData[..]
      : image.ToRgb24();

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = pixels,
      RawData = [],
      Version = CurrentVersion,
      FrameCount = 1,
      Flags = 0,
    };
  }
}
