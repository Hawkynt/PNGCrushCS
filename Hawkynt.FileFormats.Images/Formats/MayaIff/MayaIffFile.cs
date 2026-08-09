using System;
using FileFormat.Core;

namespace FileFormat.MayaIff;

/// <summary>In-memory representation of a Maya IFF (FOR4/CIMG) image.</summary>
public readonly record struct MayaIffFile : IImageFormatReader<MayaIffFile>, IImageToRawImage<MayaIffFile>, IImageFromRawImage<MayaIffFile>, IImageFormatWriter<MayaIffFile> {

  static string IImageFormatMetadata<MayaIffFile>.PrimaryExtension => ".iff";
  /// <summary>
  /// The three names one format goes by: Alias|Wavefront's own, Maya's, and Explore's.
  /// </summary>
  /// <remarks>
  /// XnView catalogues this as <c>Explore (TDI) &amp; Maya</c> reading <c>iff</c> and <c>tdi</c>,
  /// which are one decoder — Explore was TDI's renderer and Maya inherited its image format.
  /// Claiming <c>.tdi</c> is safe because the name decides nothing: the file still has to open with
  /// <c>FOR4</c>, name its form <c>CIMG</c>, carry a <c>TBHD</c>, and have every tile's coding
  /// account for that tile's chunk exactly.
  /// </remarks>
  static string[] IImageFormatMetadata<MayaIffFile>.FileExtensions => [".iff", ".maya", ".tdi"];
  static MayaIffFile IImageFormatReader<MayaIffFile>.FromSpan(ReadOnlySpan<byte> data) => MayaIffReader.FromSpan(data);

  static bool? IImageFormatMetadata<MayaIffFile>.MatchesSignature(ReadOnlySpan<byte> header)
    => header.Length >= 12 && header[0] == 0x46 && header[1] == 0x4F && header[2] == 0x52 && header[3] == 0x34
      && header[8] == 0x43 && header[9] == 0x49 && header[10] == 0x4D && header[11] == 0x47;

  static byte[] IImageFormatWriter<MayaIffFile>.ToBytes(MayaIffFile file) => MayaIffWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Whether the image contains an alpha channel (RGBA vs RGB).</summary>
  public bool HasAlpha { get; init; }

  /// <summary>Raw pixel data: RGBA (4 bpp) when <see cref="HasAlpha"/> is true, RGB (3 bpp) otherwise.</summary>
  public byte[] PixelData { get; init; }

  public static RawImage ToRawImage(MayaIffFile file) {
    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = file.HasAlpha ? PixelFormat.Rgba32 : PixelFormat.Rgb24,
      PixelData = file.PixelData[..],
    };
  }

  public static MayaIffFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format is not (PixelFormat.Rgba32 or PixelFormat.Rgb24))
      throw new ArgumentException($"Expected {PixelFormat.Rgba32} or {PixelFormat.Rgb24} but got {image.Format}.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      HasAlpha = image.Format == PixelFormat.Rgba32,
      PixelData = image.PixelData[..],
    };
  }
}
