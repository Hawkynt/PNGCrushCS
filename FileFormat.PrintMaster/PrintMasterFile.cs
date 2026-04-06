using System;
using FileFormat.Core;

namespace FileFormat.PrintMaster;

/// <summary>In-memory representation of a Print Master graphics clip art image.</summary>
public readonly record struct PrintMasterFile : IImageFormatReader<PrintMasterFile>, IImageToRawImage<PrintMasterFile>, IImageFromRawImage<PrintMasterFile>, IImageFormatWriter<PrintMasterFile> {

  /// <summary>Header size: uint16 LE width-in-bytes + uint16 LE height = 4 bytes.</summary>
  internal const int HeaderSize = 4;

  static string IImageFormatMetadata<PrintMasterFile>.PrimaryExtension => ".pm";
  static string[] IImageFormatMetadata<PrintMasterFile>.FileExtensions => [".pm"];
  static PrintMasterFile IImageFormatReader<PrintMasterFile>.FromSpan(ReadOnlySpan<byte> data) => PrintMasterReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<PrintMasterFile>.Capabilities => FormatCapability.MonochromeOnly;
  static byte[] IImageFormatWriter<PrintMasterFile>.ToBytes(PrintMasterFile file) => PrintMasterWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Packed 1bpp pixel data.</summary>
  public byte[] PixelData { get; init; }

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  /// <summary>Converts to Indexed1 raw image with B&amp;W palette.</summary>
  public static RawImage ToRawImage(PrintMasterFile file) {

    var bytesPerRow = (file.Width + 7) / 8;
    var expectedSize = bytesPerRow * file.Height;
    var pixelData = new byte[expectedSize];
    file.PixelData.AsSpan(0, Math.Min(file.PixelData.Length, expectedSize)).CopyTo(pixelData);

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Indexed1,
      PixelData = pixelData,
      Palette = _BlackWhitePalette[..],
      PaletteCount = 2,
    };
  }

  /// <summary>Creates a Print Master image from an Indexed1 raw image.</summary>
  public static PrintMasterFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException($"Expected {PixelFormat.Indexed1} but got {image.Format}.", nameof(image));

    var pixelData = image.PixelData[..];

    return new() { Width = image.Width, Height = image.Height, PixelData = pixelData };
  }
}
