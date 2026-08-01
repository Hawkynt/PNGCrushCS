using System;
using FileFormat.Core;

namespace FileFormat.Ipl;

/// <summary>In-memory representation of an IPLab image.</summary>
/// <remarks>
/// A tagged format: four-character tags, each with a length, so a reader steps through them rather
/// than reading fixed offsets. The picture is stored one channel at a time rather than interleaved,
/// and its samples are commonly sixteen bits.
/// </remarks>
public readonly record struct IplFile : IImageFormatReader<IplFile>, IImageToRawImage<IplFile>, IImageFromRawImage<IplFile>, IImageFormatWriter<IplFile> {

  /// <summary>The tags and sizes that precede the samples.</summary>
  internal const int HeaderSize = 44;

  /// <summary>Marks a file whose numbers are little-endian.</summary>
  internal const string IntelMagic = "iiii";

  /// <summary>Marks a file whose numbers are big-endian.</summary>
  internal const string MotorolaMagic = "mmmm";

  /// <summary>The only version this reads.</summary>
  internal const string Version = "100f";

  /// <summary>The tag the picture itself sits under.</summary>
  internal const string DataTag = "data";

  /// <summary>The tag that closes the file.</summary>
  internal const string EndTag = "fini";

  static string IImageFormatMetadata<IplFile>.PrimaryExtension => ".ipl";
  static string[] IImageFormatMetadata<IplFile>.FileExtensions => [".ipl"];
  static IplFile IImageFormatReader<IplFile>.FromSpan(ReadOnlySpan<byte> data) => IplReader.FromSpan(data);
  static byte[] IImageFormatWriter<IplFile>.ToBytes(IplFile file) => IplWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Channels, stored one whole plane after another rather than interleaved.</summary>
  public int Channels { get; init; }

  /// <summary>Bits a sample: eight or sixteen.</summary>
  public int SampleBits { get; init; }

  /// <summary>Whether the samples are big-endian, which the magic states.</summary>
  public bool IsBigEndian { get; init; }

  /// <summary>The planes, back to back.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(IplFile file) {
    var pixels = file.Width * file.Height;
    var channels = Math.Max(1, file.Channels);
    var step = file.SampleBits / 8;
    var result = new byte[pixels * (channels == 1 ? 1 : 3)];

    for (var i = 0; i < pixels; ++i)
    for (var c = 0; c < channels && c < 3; ++c) {
      var at = (c * pixels + i) * step;
      if (at >= file.PixelData.Length)
        break;

      // Deeper samples are narrowed by taking the top byte rather than by rounding, which can
      // carry a sample past the neighbour it is meant to stay below.
      var value = step == 2 && at + 1 < file.PixelData.Length
        ? file.IsBigEndian ? file.PixelData[at] : file.PixelData[at + 1]
        : file.PixelData[at];

      result[channels == 1 ? i : i * 3 + c] = value;
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = channels == 1 ? PixelFormat.Gray8 : PixelFormat.Rgb24,
      PixelData = result,
    };
  }

  public static IplFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureFormat(PixelFormat.Rgb24);

    var pixels = image.Width * image.Height;
    var planes = new byte[pixels * 3];
    for (var i = 0; i < pixels; ++i)
    for (var c = 0; c < 3; ++c)
      planes[c * pixels + i] = image.PixelData[i * 3 + c];

    return new() {
      Width = image.Width,
      Height = image.Height,
      Channels = 3,
      SampleBits = 8,
      PixelData = planes,
    };
  }
}
