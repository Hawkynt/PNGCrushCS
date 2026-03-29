using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Rgf;

/// <summary>In-memory representation of an RGF (LEGO Mindstorms EV3) image.</summary>
public sealed class RgfFile : IImageFileFormat<RgfFile> {

  static string IImageFileFormat<RgfFile>.PrimaryExtension => ".rgf";
  static string[] IImageFileFormat<RgfFile>.FileExtensions => [".rgf"];
  static FormatCapability IImageFileFormat<RgfFile>.Capabilities => FormatCapability.MonochromeOnly;
  static RgfFile IImageFileFormat<RgfFile>.FromFile(FileInfo file) => RgfReader.FromFile(file);
  static RgfFile IImageFileFormat<RgfFile>.FromBytes(byte[] data) => RgfReader.FromBytes(data);
  static RgfFile IImageFileFormat<RgfFile>.FromStream(Stream stream) => RgfReader.FromStream(stream);
  static RawImage IImageFileFormat<RgfFile>.ToRawImage(RgfFile file) => file.ToRawImage();
  static byte[] IImageFileFormat<RgfFile>.ToBytes(RgfFile file) => RgfWriter.ToBytes(file);

  /// <summary>Image width in pixels (1-178).</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels (1-128).</summary>
  public int Height { get; init; }

  /// <summary>1bpp packed pixel data, MSB first, ceil(width/8) bytes per row.</summary>
  public byte[] PixelData { get; init; } = [];

  private static readonly byte[] _BlackWhitePalette = [0, 0, 0, 255, 255, 255];

  public RawImage ToRawImage() => new() {
    Width = this.Width,
    Height = this.Height,
    Format = PixelFormat.Indexed1,
    PixelData = this.PixelData[..],
    Palette = _BlackWhitePalette[..],
    PaletteCount = 2,
  };

  public static RgfFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed1.", nameof(image));

    return new() {
      Width = image.Width,
      Height = image.Height,
      PixelData = image.PixelData[..],
    };
  }
}
