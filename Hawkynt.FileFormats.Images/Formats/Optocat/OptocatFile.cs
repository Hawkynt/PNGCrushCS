using System;
using FileFormat.Core;

namespace FileFormat.Optocat;

/// <summary>An Optocat picture (.abs): a byte-order word, a handful of shorts and uncompressed rows.</summary>
/// <remarks>
/// Optocat is Breuckmann's scanner software. The verified form has nine shorts in the byte order
/// announced by <c>II</c> or <c>MM</c>, including a pixel offset, samples per pixel, width and height.
/// The rows are uncompressed. This writer uses the lossless three-sample RGB form at offset 2048.
/// </remarks>
[FormatDetectionPriority(999)]
public readonly record struct OptocatFile
  : IImageFormatReader<OptocatFile>, IImageToRawImage<OptocatFile>, IImageFromRawImage<OptocatFile>, IImageFormatWriter<OptocatFile> {

  public const int MinimumOffset = 2048;
  public const int HeaderSize = 18;
  public const int MinimumSamples = 1;
  public const int MaximumSamples = 4;

  static string IImageFormatMetadata<OptocatFile>.PrimaryExtension => ".abs";
  static string[] IImageFormatMetadata<OptocatFile>.FileExtensions => [".abs"];
  static OptocatFile IImageFormatReader<OptocatFile>.FromSpan(ReadOnlySpan<byte> data) => OptocatReader.FromSpan(data);
  static byte[] IImageFormatWriter<OptocatFile>.ToBytes(OptocatFile file) => OptocatWriter.ToBytes(file);
  static VideoMode[] IImageFormatMetadata<OptocatFile>.VideoModes => [
    new("Optocat", [(IntegerRange.Any, IntegerRange.Any)], [256, 32768, 16777216])
  ];

  static bool? IImageFormatMetadata<OptocatFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length < HeaderSize)
      return null;

    var littleEndian = header[0] == (byte)'I' && header[1] == (byte)'I';
    if (!littleEndian && !(header[0] == (byte)'M' && header[1] == (byte)'M'))
      return null;

    var offset = _Word(header, 4, littleEndian);
    var samples = _Word(header, 10, littleEndian);
    var width = _Word(header, 14, littleEndian);
    var height = _Word(header, 16, littleEndian);
    if (offset < MinimumOffset || samples is < MinimumSamples or > MaximumSamples || width == 0 || height == 0)
      return null;

    if (header.Length > MinimumOffset) {
      var need = (long)offset + (long)height * width * samples;
      if (need > header.Length)
        return null;
    }

    return true;
  }

  private static int _Word(ReadOnlySpan<byte> data, int at, bool littleEndian)
    => littleEndian ? data[at] | (data[at + 1] << 8) : (data[at] << 8) | data[at + 1];

  public bool IsLittleEndian { get; init; }
  public int Width { get; init; }
  public int Height { get; init; }
  public int SamplesPerPixel { get; init; }
  public int PixelOffset { get; init; }
  public byte[] PixelData { get; init; }
  public int BytesPerRow => (this.Width * this.SamplesPerPixel * 8 + 7) / 8;

  public static RawImage ToRawImage(OptocatFile file) {
    var source = file.PixelData;
    if (source == null)
      throw new InvalidOperationException("No Optocat picture was read.");

    var width = file.Width;
    var height = file.Height;
    var stride = file.BytesPerRow;
    var count = width * height;

    switch (file.SamplesPerPixel) {
      case 1:
        return new() { Width = width, Height = height, Format = PixelFormat.Gray8, PixelData = source[..] };
      case 2: {
        var pixels = new byte[count * 3];
        for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x) {
          var at = y * stride + x * 2;
          var value = source[at] | (source[at + 1] << 8);
          var to = (y * width + x) * 3;
          pixels[to] = (byte)(((value >> 10) & 31) * 255 / 31);
          pixels[to + 1] = (byte)(((value >> 5) & 31) * 255 / 31);
          pixels[to + 2] = (byte)((value & 31) * 255 / 31);
        }

        return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
      }
      case 3:
        return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = source[..] };
      case 4: {
        var pixels = new byte[count * 3];
        for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x) {
          var at = y * stride + x * 4;
          var to = (y * width + x) * 3;
          pixels[to] = source[at];
          pixels[to + 1] = source[at + 1];
          pixels[to + 2] = source[at + 2];
        }

        return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
      }
      default:
        throw new InvalidOperationException($"Optocat: {file.SamplesPerPixel} samples a pixel is not one this reads.");
    }
  }

  /// <summary>Creates the externally verified, uncompressed RGB representation.</summary>
  public static OptocatFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Width is < 1 or > ushort.MaxValue || image.Height is < 1 or > ushort.MaxValue)
      throw new ArgumentException($"Optocat dimensions must fit 16-bit fields; got {image.Width}x{image.Height}.", nameof(image));

    var rgb = image.EnsureFormat(PixelFormat.Rgb24);
    return new() {
      IsLittleEndian = true,
      Width = rgb.Width,
      Height = rgb.Height,
      SamplesPerPixel = 3,
      PixelOffset = MinimumOffset,
      PixelData = rgb.PixelData[..],
    };
  }
}
