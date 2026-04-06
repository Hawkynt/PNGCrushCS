using System;
using FileFormat.Core;

namespace FileFormat.PabloPaint;

/// <summary>In-memory representation of an Atari ST Pablo Paint image (640x400, monochrome).</summary>
public readonly record struct PabloPaintFile : IImageFormatReader<PabloPaintFile>, IImageToRawImage<PabloPaintFile>, IImageFromRawImage<PabloPaintFile>, IImageFormatWriter<PabloPaintFile> {

  /// <summary>Image width (always 640).</summary>
  internal const int PixelWidth = 640;

  /// <summary>Image height (always 400).</summary>
  internal const int PixelHeight = 400;

  /// <summary>Exact file size in bytes (640/8 * 400 = 32000).</summary>
  internal const int FileSize = PixelWidth / 8 * PixelHeight;

  static string IImageFormatMetadata<PabloPaintFile>.PrimaryExtension => ".pa3";
  static string[] IImageFormatMetadata<PabloPaintFile>.FileExtensions => [".pa3"];
  static PabloPaintFile IImageFormatReader<PabloPaintFile>.FromSpan(ReadOnlySpan<byte> data) => PabloPaintReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<PabloPaintFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<PabloPaintFile>.ToBytes(PabloPaintFile file) => PabloPaintWriter.ToBytes(file);

  /// <summary>Always 640.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 400.</summary>
  public int Height => PixelHeight;

  /// <summary>32000 bytes of raw monochrome bitmap data. Each byte = 8 pixels, MSB first. 0=white, 1=black.</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Black-and-white palette: index 0 = white, index 1 = black.</summary>
  private static readonly byte[] _Palette = [255, 255, 255, 0, 0, 0];

  /// <summary>Converts the monochrome bitmap to an Indexed1 raw image (640x400, B&amp;W palette).</summary>
  public static RawImage ToRawImage(PabloPaintFile file) {

    var rowStride = PixelWidth / 8; // 80 bytes per row
    var pixelData = new byte[rowStride * PixelHeight];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, pixelData.Length)).CopyTo(pixelData);

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed1,
      PixelData = pixelData,
      Palette = _Palette[..],
      PaletteCount = 2,
    };
  }

  /// <summary>Creates a Pablo Paint file from an Indexed1 raw image (640x400).</summary>
  public static PabloPaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected {PixelFormat.Indexed1} but got {image.Format}.", nameof(image));
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var pixelData = new byte[FileSize];
    image.PixelData.AsSpan(0, Math.Min(image.PixelData.Length, FileSize)).CopyTo(pixelData);

    return new() { PixelData = pixelData };
  }
}
