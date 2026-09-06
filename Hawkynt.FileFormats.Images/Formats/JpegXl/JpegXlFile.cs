using System;
using FileFormat.Core;

namespace FileFormat.JpegXl;

/// <summary>In-memory representation of a JPEG XL image.</summary>
/// <remarks>
/// JPEG XL container and codestream metadata are parsed according to ISO/IEC 18181. The decoder
/// implements the real modular and VarDCT paths and refuses unsupported syntax instead of returning
/// placeholders.
/// <para/>
/// The writer emits a lossless modular codestream: one frame, samples predicted from their
/// neighbours and entropy-coded, no colour transform and nothing quantised. That covers grey and
/// colour at eight and sixteen bits, with or without alpha, at any size — a picture larger than a
/// group is stated a group at a time, which is what the format asks for. Lossy coding is read but
/// not written.
/// </remarks>
public readonly record struct JpegXlFile
  : IImageFormatReader<JpegXlFile>, IImageFormatWriter<JpegXlFile>,
    IImageToRawImage<JpegXlFile>, IImageFromRawImage<JpegXlFile>,
    IMultiImageFileFormat<JpegXlFile> {

  static string IImageFormatMetadata<JpegXlFile>.PrimaryExtension => ".jxl";
  static string[] IImageFormatMetadata<JpegXlFile>.FileExtensions => [".jxl"];
  static JpegXlFile IImageFormatReader<JpegXlFile>.FromSpan(ReadOnlySpan<byte> data) => JpegXlReader.FromSpan(data);
  static byte[] IImageFormatWriter<JpegXlFile>.ToBytes(JpegXlFile file) => JpegXlWriter.ToBytes(file);
  static FormatCapability IImageFormatMetadata<JpegXlFile>.Capabilities => FormatCapability.MultiImage;

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

  /// <summary>
  /// Every moment of an animation, in order and each the whole picture at that
  /// moment, laid out exactly as <see cref="PixelData"/> is. Empty for a still
  /// picture, and <see cref="PixelData"/> is the first of these when it is not.
  /// </summary>
  /// <remarks>
  /// A frame the file states with no duration is a layer of the one after it
  /// rather than a moment of its own, so these are the frames a viewer would
  /// actually show and not every frame the file contains.
  /// </remarks>
  public byte[][] Frames { get; init; }

  /// <summary>Container major brand, or <c>"jxl "</c> for a bare codestream.</summary>
  public string Brand { get; init; }

  /// <summary>How many moments this file states: one for a still picture.</summary>
  public static int ImageCount(JpegXlFile file) => file.Frames is { Length: > 0 } frames ? frames.Length : 1;

  /// <summary>The picture at one moment of an animation.</summary>
  public static RawImage ToRawImage(JpegXlFile file, int index) {
    if ((uint)index >= (uint)ImageCount(file))
      throw new ArgumentOutOfRangeException(nameof(index), $"This JPEG XL file states {ImageCount(file)} moments, so there is no moment {index}.");

    return ToRawImage(file.Frames is { Length: > 0 } frames ? file with { PixelData = frames[index] } : file);
  }

  public static RawImage ToRawImage(JpegXlFile file) {
    var deep = file.BitsPerSample == 16;
    var format = (file.ComponentCount, deep) switch {
      (1, false) => PixelFormat.Gray8,
      (2, false) => PixelFormat.GrayAlpha16,
      (3, false) => PixelFormat.Rgb24,
      (4, false) => PixelFormat.Rgba32,
      (1, true) => PixelFormat.Gray16,
      (2, true) => PixelFormat.GrayAlpha32,
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
