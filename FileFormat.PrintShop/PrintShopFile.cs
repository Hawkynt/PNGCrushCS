using System;
using FileFormat.Core;

namespace FileFormat.PrintShop;

/// <summary>In-memory representation of a Print Shop graphics clip art image (PSA/PSB).</summary>
public readonly record struct PrintShopFile : IImageFormatReader<PrintShopFile>, IImageToRawImage<PrintShopFile>, IImageFromRawImage<PrintShopFile>, IImageFormatWriter<PrintShopFile> {

  /// <summary>Fixed pixel width.</summary>
  internal const int PixelWidth = 88;

  /// <summary>Fixed pixel height.</summary>
  internal const int PixelHeight = 52;

  /// <summary>Bytes per pixel row (88 / 8 = 11).</summary>
  internal const int BytesPerRow = 11;

  /// <summary>Total pixel data size in bytes (11 * 52 = 572).</summary>
  internal const int PixelDataSize = BytesPerRow * PixelHeight;

  /// <summary>PSA file size (raw data, no header).</summary>
  internal const int PsaFileSize = 572;

  /// <summary>PSB file size (4-byte header + raw data).</summary>
  internal const int PsbFileSize = 576;

  /// <summary>PSB header size.</summary>
  internal const int PsbHeaderSize = 4;

  static string IImageFormatMetadata<PrintShopFile>.PrimaryExtension => ".psa";
  static string[] IImageFormatMetadata<PrintShopFile>.FileExtensions => [".psa", ".psb"];
  static PrintShopFile IImageFormatReader<PrintShopFile>.FromSpan(ReadOnlySpan<byte> data) => PrintShopReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<PrintShopFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<PrintShopFile>.ToBytes(PrintShopFile file) => PrintShopWriter.ToBytes(file);

  /// <summary>Always 88.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 52.</summary>
  public int Height => PixelHeight;

  /// <summary>Packed 1bpp pixel data (572 bytes).</summary>
  public byte[] PixelData { get; init; }

  /// <summary>Whether this was loaded from a PSB (format B with header) file.</summary>
  public bool IsFormatB { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts the Print Shop image to an Indexed1 raw image.</summary>
  public static RawImage ToRawImage(PrintShopFile file) {

    var pixelData = new byte[PixelDataSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, PixelDataSize)).CopyTo(pixelData);

    return new() {
      Width = PixelWidth,
      Height = PixelHeight,
      Format = PixelFormat.Indexed1,
      PixelData = pixelData,
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  /// <summary>Creates a Print Shop image from an Indexed1 raw image (88x52).</summary>
  public static PrintShopFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected {PixelFormat.Indexed1} but got {image.Format}.", nameof(image));
    if (image.Width != PixelWidth || image.Height != PixelHeight)
      throw new ArgumentException($"Expected {PixelWidth}x{PixelHeight} but got {image.Width}x{image.Height}.", nameof(image));

    var pixelData = new byte[PixelDataSize];
    image.PixelData.AsSpan(0, Math.Min(image.PixelData.Length, PixelDataSize)).CopyTo(pixelData);
    return new() { PixelData = pixelData };
  }
}
