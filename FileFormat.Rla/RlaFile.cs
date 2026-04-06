using System;
using FileFormat.Core;

namespace FileFormat.Rla;

/// <summary>In-memory representation of a Wavefront RLA image.</summary>
public readonly record struct RlaFile : IImageFormatReader<RlaFile>, IImageToRawImage<RlaFile>, IImageFromRawImage<RlaFile>, IImageFormatWriter<RlaFile> {

  static string IImageFormatMetadata<RlaFile>.PrimaryExtension => ".rla";
  static string[] IImageFormatMetadata<RlaFile>.FileExtensions => [".rla", ".rlb", ".rpf"];
  static RlaFile IImageFormatReader<RlaFile>.FromSpan(ReadOnlySpan<byte> data) => RlaReader.FromSpan(data);
  static byte[] IImageFormatWriter<RlaFile>.ToBytes(RlaFile file) => RlaWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int NumChannels { get; init; }
  public int NumMatte { get; init; }
  public int NumBits { get; init; }
  public int StorageType { get; init; }
  public int FrameNumber { get; init; }
  public string Description { get; init; }
  public string ProgramName { get; init; }

  /// <summary>Raw pixel data stored in channel-planar order per scanline (bottom-to-top, channel-interleaved per scanline).</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(RlaFile file) {

    PixelFormat format;
    int bytesPerPixel;
    if (file.NumChannels >= 3 && file.NumMatte >= 1) {
      format = PixelFormat.Rgba32;
      bytesPerPixel = 4;
    } else if (file.NumChannels >= 3) {
      format = PixelFormat.Rgb24;
      bytesPerPixel = 3;
    } else
      throw new ArgumentException($"Unsupported channel configuration: NumChannels={file.NumChannels}, NumMatte={file.NumMatte}", nameof(file));

    var stride = file.Width * bytesPerPixel;
    var flipped = _FlipRows(file.PixelData, stride, file.Height);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = format,
      PixelData = flipped,
    };
  }

  public static RlaFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    int numChannels;
    int numMatte;
    int bytesPerPixel;
    switch (image.Format) {
      case PixelFormat.Rgb24:
        numChannels = 3;
        numMatte = 0;
        bytesPerPixel = 3;
        break;
      case PixelFormat.Rgba32:
        numChannels = 3;
        numMatte = 1;
        bytesPerPixel = 4;
        break;
      default:
        throw new ArgumentException($"Unsupported pixel format for RLA: {image.Format}", nameof(image));
    }

    var stride = image.Width * bytesPerPixel;
    var flipped = _FlipRows(image.PixelData, stride, image.Height);

    return new() {
      Width = image.Width,
      Height = image.Height,
      NumChannels = numChannels,
      NumMatte = numMatte,
      NumBits = 8,
      StorageType = 0,
      PixelData = flipped,
    };
  }

  private static byte[] _FlipRows(byte[] data, int stride, int height) {
    var result = new byte[data.Length];
    for (var y = 0; y < height; ++y)
      data.AsSpan(y * stride, stride).CopyTo(result.AsSpan((height - 1 - y) * stride));
    return result;
  }
}
