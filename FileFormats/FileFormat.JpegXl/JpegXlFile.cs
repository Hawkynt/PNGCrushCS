using System;
using FileFormat.Core;

namespace FileFormat.JpegXl;

/// <summary>In-memory representation of a JPEG XL image.
///
/// <para><b>Codec-scope honesty:</b> This implementation handles the JPEG XL
/// <em>container</em> (FF 0A bare codestream signature, ISOBMFF jxl/jxlc/jxlp boxes,
/// SizeHeader per ISO/IEC 18181-1 §3.6.2) in spec-conformant fashion — real JPEG XL
/// files produced by libjxl will be detected and their dimensions correctly extracted.
/// </para>
///
/// <para>However, the <em>pixel codec</em> (modular sub-codec frame payload, VarDCT)
/// is not yet a spec-conformant implementation of ISO/IEC 18181-1. The current
/// <c>JxlFrameEncoder</c>/<c>JxlFrameDecoder</c> use a simplified internal layout
/// that round-trips between this library's own writer/reader but will NOT decode
/// arbitrary real-world JPEG XL files, nor produce output that real JPEG XL viewers
/// (libjxl, browsers, etc.) can decode. Pixel-perfect interop with real JPEG XL is
/// a future workstream — track via the README "Limitations" section.</para>
///
/// <para>For the meantime, use this for: (1) detecting JPEG XL files by signature,
/// (2) extracting dimensions from the SizeHeader of real JPEG XL files,
/// (3) round-tripping through this library's own format. For (4) decoding
/// arbitrary real-world JPEG XL pixel data — use libjxl via P/Invoke or a
/// future spec-compliant codec.</para>
/// </summary>
public readonly record struct JpegXlFile : IImageFormatReader<JpegXlFile>, IImageToRawImage<JpegXlFile>, IImageFromRawImage<JpegXlFile>, IImageFormatWriter<JpegXlFile> {

  static string IImageFormatMetadata<JpegXlFile>.PrimaryExtension => ".jxl";
  static string[] IImageFormatMetadata<JpegXlFile>.FileExtensions => [".jxl"];
  static JpegXlFile IImageFormatReader<JpegXlFile>.FromSpan(ReadOnlySpan<byte> data) => JpegXlReader.FromSpan(data);
  static byte[] IImageFormatWriter<JpegXlFile>.ToBytes(JpegXlFile file) => JpegXlWriter.ToBytes(file);

  static bool? IImageFormatMetadata<JpegXlFile>.MatchesSignature(ReadOnlySpan<byte> header) {
    if (header.Length >= 2 && header[0] == 0xFF && header[1] == 0x0A)
      return true;
    if (header.Length >= 12 && header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70
        && header[8] == (byte)'j' && header[9] == (byte)'x' && header[10] == (byte)'l' && header[11] == (byte)' ')
      return true;
    return null;
  }

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Number of color components (1 for grayscale, 3 for RGB).</summary>
  public int ComponentCount { get; init; }

  /// <summary>Raw pixel data (Gray8 or Rgb24 layout).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>ISOBMFF brand string (default "jxl ").</summary>
  public string Brand { get; init; }

  public static RawImage ToRawImage(JpegXlFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = file.ComponentCount == 1 ? PixelFormat.Gray8 : PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static JpegXlFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    image = image.EnsureAnyFormat(PixelFormat.Rgb24, PixelFormat.Gray8);

    int componentCount;
    if (image.Format == PixelFormat.Gray8)
      componentCount = 1;
    else if (image.Format == PixelFormat.Rgb24)
      componentCount = 3;
    else
      throw new ArgumentException($"Expected {PixelFormat.Gray8} or {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      ComponentCount = componentCount,
      PixelData = image.PixelData[..],
      Brand = "jxl ",
    };
  }
}
