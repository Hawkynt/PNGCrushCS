using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.PabloPaint;

/// <summary>In-memory representation of an Atari ST Pablo Paint image (640x400, monochrome).</summary>
public sealed class PabloPaintFile : IImageFileFormat<PabloPaintFile> {

  /// <summary>Image width (always 640).</summary>
  internal const int PixelWidth = 640;

  /// <summary>Image height (always 400).</summary>
  internal const int PixelHeight = 400;

  /// <summary>Exact file size in bytes (640/8 * 400 = 32000).</summary>
  internal const int FileSize = PixelWidth / 8 * PixelHeight;

  static string IImageFileFormat<PabloPaintFile>.PrimaryExtension => ".pa3";
  static string[] IImageFileFormat<PabloPaintFile>.FileExtensions => [".pa3"];
  static FormatCapability IImageFileFormat<PabloPaintFile>.Capabilities => FormatCapability.MonochromeOnly;
  static PabloPaintFile IImageFileFormat<PabloPaintFile>.FromFile(FileInfo file) => PabloPaintReader.FromFile(file);
  static PabloPaintFile IImageFileFormat<PabloPaintFile>.FromBytes(byte[] data) => PabloPaintReader.FromBytes(data);
  static PabloPaintFile IImageFileFormat<PabloPaintFile>.FromStream(Stream stream) => PabloPaintReader.FromStream(stream);
  static byte[] IImageFileFormat<PabloPaintFile>.ToBytes(PabloPaintFile file) => PabloPaintWriter.ToBytes(file);

  /// <summary>Always 640.</summary>
  public int Width => PixelWidth;

  /// <summary>Always 400.</summary>
  public int Height => PixelHeight;

  /// <summary>32000 bytes of raw monochrome bitmap data. Each byte = 8 pixels, MSB first. 0=white, 1=black.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Black-and-white palette: index 0 = white, index 1 = black.</summary>
  private static readonly byte[] _Palette = [255, 255, 255, 0, 0, 0];

  /// <summary>Converts the monochrome bitmap to an Indexed1 raw image (640x400, B&amp;W palette).</summary>
  public static RawImage ToRawImage(PabloPaintFile file) {
    ArgumentNullException.ThrowIfNull(file);

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
