using System;
using FileFormat.Core;

namespace FileFormat.JpegXl;

/// <summary>In-memory representation of a JPEG XL image.</summary>
/// <remarks>
/// JPEG XL container and codestream metadata are parsed according to ISO/IEC 18181. The writer uses
/// the standard lossless modular profile ported from libjxl/zune-jpegxl rather than the former private
/// <c>0x4D</c> payload. The decoder supports the real modular and VarDCT paths implemented by the
/// codec classes in this package and refuses unsupported syntax instead of returning placeholders.
/// </remarks>
public readonly record struct JpegXlFile : IImageFormatReader<JpegXlFile>, IImageToRawImage<JpegXlFile>, IImageFromRawImage<JpegXlFile>, IImageFormatWriter<JpegXlFile> {

  static string IImageFormatMetadata<JpegXlFile>.PrimaryExtension => ".jxl";
  static string[] IImageFormatMetadata<JpegXlFile>.FileExtensions => [".jxl"];
  static JpegXlFile IImageFormatReader<JpegXlFile>.FromSpan(ReadOnlySpan<byte> data) => JpegXlReader.FromSpan(data);
  static byte[] IImageFormatWriter<JpegXlFile>.ToBytes(JpegXlFile file) => JpegXlWriter.ToBytes(file);

  static bool? IImageFormatMetadata<JpegXlFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length >= 2 && header[0] == 0xFF && header[1] == 0x0A)
      return true;
    // ISO BMFF JPEG XL signature box.
    if (header.Length >= 12
        && header[0] == 0x00 && header[1] == 0x00 && header[2] == 0x00 && header[3] == 0x0C
        && header[4] == (byte)'J' && header[5] == (byte)'X' && header[6] == (byte)'L' && header[7] == (byte)' '
        && header[8] == 0x0D && header[9] == 0x0A && header[10] == 0x87 && header[11] == 0x0A)
      return true;
    // Older/simple containers encountered in the corpus may begin directly with ftyp.
    if (header.Length >= 12 && header[4] == (byte)'f' && header[5] == (byte)'t' && header[6] == (byte)'y' && header[7] == (byte)'p'
        && header[8] == (byte)'j' && header[9] == (byte)'x' && header[10] == (byte)'l' && header[11] == (byte)' ')
      return true;
    return null;
  }

  public int Width { get; init; }
  public int Height { get; init; }

  /// <summary>Interleaved 8-bit component count: 1=Gray, 2=Gray+Alpha, 3=RGB, 4=RGBA.</summary>
  public int ComponentCount { get; init; }

  /// <summary>Interleaved 8-bit pixels.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Container major brand, or <c>"jxl "</c> for a bare codestream.</summary>
  public string Brand { get; init; }

  public static RawImage ToRawImage(JpegXlFile file) {
    var format = file.ComponentCount switch {
      1 => PixelFormat.Gray8,
      2 => PixelFormat.GrayAlpha16,
      3 => PixelFormat.Rgb24,
      4 => PixelFormat.Rgba32,
      _ => throw new NotSupportedException($"JPEG XL component count {file.ComponentCount} is not supported by RawImage."),
    };
    var needed = checked((long)file.Width * file.Height * file.ComponentCount);
    if (file.PixelData == null || file.PixelData.LongLength < needed)
      throw new InvalidOperationException(
        $"JPEG XL decoder returned an incomplete raster: {file.Width}x{file.Height}x{file.ComponentCount} needs {needed} bytes.");

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = format,
      PixelData = file.PixelData[..checked((int)needed)],
    };
  }

  public static JpegXlFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureAnyFormat(PixelFormat.Rgba32, PixelFormat.Rgb24, PixelFormat.GrayAlpha16, PixelFormat.Gray8);

    var componentCount = image.Format switch {
      PixelFormat.Gray8 => 1,
      PixelFormat.GrayAlpha16 => 2,
      PixelFormat.Rgb24 => 3,
      PixelFormat.Rgba32 => 4,
      _ => throw new ArgumentException($"Unsupported JPEG XL source format {image.Format}.", nameof(image)),
    };

    return new() {
      Width = image.Width,
      Height = image.Height,
      ComponentCount = componentCount,
      PixelData = image.PixelData[..],
      Brand = "jxl ",
    };
  }
}
