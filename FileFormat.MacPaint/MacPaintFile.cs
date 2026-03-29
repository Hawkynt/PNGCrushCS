using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MacPaint;

/// <summary>In-memory representation of a MacPaint image.</summary>
public sealed class MacPaintFile : IImageFileFormat<MacPaintFile> {

  static string IImageFileFormat<MacPaintFile>.PrimaryExtension => ".mac";
  static string[] IImageFileFormat<MacPaintFile>.FileExtensions => [".mac", ".macp", ".pntg", ".pnt", ".paint", ".mpnt"];
  static FormatCapability IImageFileFormat<MacPaintFile>.Capabilities => FormatCapability.MonochromeOnly;
  static MacPaintFile IImageFileFormat<MacPaintFile>.FromFile(FileInfo file) => MacPaintReader.FromFile(file);
  static MacPaintFile IImageFileFormat<MacPaintFile>.FromBytes(byte[] data) => MacPaintReader.FromBytes(data);
  static MacPaintFile IImageFileFormat<MacPaintFile>.FromStream(Stream stream) => MacPaintReader.FromStream(stream);
  static RawImage IImageFileFormat<MacPaintFile>.ToRawImage(MacPaintFile file) => file.ToRawImage();
  static byte[] IImageFileFormat<MacPaintFile>.ToBytes(MacPaintFile file) => MacPaintWriter.ToBytes(file);
  /// <summary>Image width in pixels (always 576).</summary>
  public int Width { get; init; } = 576;
  /// <summary>Image height in pixels (always 720).</summary>
  public int Height { get; init; } = 720;
  /// <summary>Header version (typically 0 or 2).</summary>
  public int Version { get; init; }
  /// <summary>38 brush patterns, 8 bytes each (304 bytes total).</summary>
  public byte[]? BrushPatterns { get; init; }
  /// <summary>1bpp packed pixel data, 72 bytes per row x 720 rows = 51840 bytes.</summary>
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

  public static MacPaintFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Indexed1)
      throw new ArgumentException("RawImage must use PixelFormat.Indexed1.", nameof(image));
    if (image.Width != 576)
      throw new ArgumentException("MacPaint images must be exactly 576 pixels wide.", nameof(image));
    if (image.Height != 720)
      throw new ArgumentException("MacPaint images must be exactly 720 pixels tall.", nameof(image));

    return new() {
      Width = 576,
      Height = 720,
      PixelData = image.PixelData[..],
    };
  }
}
