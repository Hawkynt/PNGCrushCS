using System;
using FileFormat.Core;

namespace FileFormat.JpegXl;

/// <summary>In-memory representation of a JPEG XL image.</summary>
/// <remarks>
/// JPEG XL container and codestream metadata are parsed according to ISO/IEC 18181. The decoder
/// implements the real modular and VarDCT paths and refuses unsupported syntax instead of returning
/// placeholders.
/// <para/>
/// This format registers a reader only. <see cref="JpegXlWriter"/> still exists and the codec tests
/// still drive it, but it is not reachable through <c>FormatRegistry</c>: what it assembles is a
/// <c>0x4D</c>-prefixed payload behind a bare component-count byte, which is this package's own
/// arrangement and not the ImageMetadata bundle ISO/IEC 18181-1 puts after the <c>SizeHeader</c>.
/// libjxl will not decode it and neither will the reader in this very folder, so registering it
/// would put a write capability in the registry that no decoder on earth can honour. It becomes a
/// writer again when it emits a codestream <c>djxl</c> accepts, and the test that pins that is the
/// one that has to change with it.
/// </remarks>
public readonly record struct JpegXlFile : IImageFormatReader<JpegXlFile>, IImageToRawImage<JpegXlFile> {

  static string IImageFormatMetadata<JpegXlFile>.PrimaryExtension => ".jxl";
  static string[] IImageFormatMetadata<JpegXlFile>.FileExtensions => [".jxl"];
  static JpegXlFile IImageFormatReader<JpegXlFile>.FromSpan(ReadOnlySpan<byte> data) => JpegXlReader.FromSpan(data);

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

  /// <summary>Component count: 1=Gray, 2=Gray+Alpha, 3=RGB, 4=RGBA.</summary>
  public int ComponentCount { get; init; }

  /// <summary>
  /// Bits per sample of <see cref="PixelData"/>, either 8 or 16. Sixteen means
  /// two big-endian bytes per component, which is how this package's deeper
  /// pixel formats are laid out.
  /// </summary>
  /// <remarks>
  /// Zero is read as 8, so a value built without naming a depth is still an
  /// eight-bit picture rather than an empty one.
  /// </remarks>
  public int BitsPerSample { get; init; }

  /// <summary>Interleaved pixels, at <see cref="BitsPerSample"/> per component.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Container major brand, or <c>"jxl "</c> for a bare codestream.</summary>
  public string Brand { get; init; }

  public static RawImage ToRawImage(JpegXlFile file) {
    var deep = file.BitsPerSample == 16;
    var format = (file.ComponentCount, deep) switch {
      (1, false) => PixelFormat.Gray8,
      (2, false) => PixelFormat.GrayAlpha16,
      (3, false) => PixelFormat.Rgb24,
      (4, false) => PixelFormat.Rgba32,
      (1, true) => PixelFormat.Gray16,
      (3, true) => PixelFormat.Rgb48,
      (4, true) => PixelFormat.Rgba64,
      _ => throw new NotSupportedException(
        $"JPEG XL component count {file.ComponentCount} at {file.BitsPerSample} bits is not supported by RawImage."),
    };
    var needed = checked((long)file.Width * file.Height * file.ComponentCount * (deep ? 2 : 1));
    if (file.PixelData == null || file.PixelData.LongLength < needed)
      throw new InvalidOperationException(
        $"JPEG XL decoder returned an incomplete raster: {file.Width}x{file.Height}x{file.ComponentCount} at {(deep ? 16 : 8)} bits needs {needed} bytes.");

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
      BitsPerSample = 8,
      PixelData = image.PixelData[..],
      Brand = "jxl ",
    };
  }
}
