using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Xcursor;

/// <summary>In-memory representation of an Xcursor (X11 cursor theme) image.</summary>
[FormatMagicBytes([0x58, 0x63, 0x75, 0x72])]
public sealed class XcursorFile : IImageFileFormat<XcursorFile> {

  static string IImageFileFormat<XcursorFile>.PrimaryExtension => ".xcur";
  static string[] IImageFileFormat<XcursorFile>.FileExtensions => [".xcur", ".cursor"];
  static XcursorFile IImageFileFormat<XcursorFile>.FromFile(FileInfo file) => XcursorReader.FromFile(file);
  static XcursorFile IImageFileFormat<XcursorFile>.FromBytes(byte[] data) => XcursorReader.FromBytes(data);
  static XcursorFile IImageFileFormat<XcursorFile>.FromStream(Stream stream) => XcursorReader.FromStream(stream);
  static byte[] IImageFileFormat<XcursorFile>.ToBytes(XcursorFile file) => XcursorWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Hotspot X coordinate.</summary>
  public int XHot { get; init; }

  /// <summary>Hotspot Y coordinate.</summary>
  public int YHot { get; init; }

  /// <summary>Nominal cursor size.</summary>
  public int NominalSize { get; init; }

  /// <summary>Delay for animated cursors in milliseconds.</summary>
  public int Delay { get; init; }

  /// <summary>Raw ARGB pixel data with premultiplied alpha (4 bytes per pixel, little-endian uint32 ARGB).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Converts the Xcursor image to a platform-independent <see cref="RawImage"/> with straight (unpremultiplied) alpha.</summary>
  public static RawImage ToRawImage(XcursorFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var pixelCount = file.Width * file.Height;
    var rgba = new byte[pixelCount * 4];

    for (var i = 0; i < pixelCount; ++i) {
      var srcOffset = i * 4;
      var dstOffset = i * 4;

      var b = file.PixelData[srcOffset];
      var g = file.PixelData[srcOffset + 1];
      var r = file.PixelData[srcOffset + 2];
      var a = file.PixelData[srcOffset + 3];

      if (a > 0 && a < 255) {
        r = (byte)Math.Min(255, r * 255 / a);
        g = (byte)Math.Min(255, g * 255 / a);
        b = (byte)Math.Min(255, b * 255 / a);
      }

      rgba[dstOffset] = r;
      rgba[dstOffset + 1] = g;
      rgba[dstOffset + 2] = b;
      rgba[dstOffset + 3] = a;
    }

    return new() {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgba32,
      PixelData = rgba,
    };
  }

  /// <summary>Creates an Xcursor image from a platform-independent <see cref="RawImage"/> with straight alpha, converting to premultiplied ARGB.</summary>
  public static XcursorFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    if (image.Format != PixelFormat.Rgba32)
      throw new ArgumentException($"Expected {PixelFormat.Rgba32} but got {image.Format}.", nameof(image));

    var pixelCount = image.Width * image.Height;
    var argb = new byte[pixelCount * 4];

    for (var i = 0; i < pixelCount; ++i) {
      var srcOffset = i * 4;
      var dstOffset = i * 4;

      var r = image.PixelData[srcOffset];
      var g = image.PixelData[srcOffset + 1];
      var b = image.PixelData[srcOffset + 2];
      var a = image.PixelData[srcOffset + 3];

      if (a < 255) {
        r = (byte)(r * a / 255);
        g = (byte)(g * a / 255);
        b = (byte)(b * a / 255);
      }

      argb[dstOffset] = b;
      argb[dstOffset + 1] = g;
      argb[dstOffset + 2] = r;
      argb[dstOffset + 3] = a;
    }

    return new() {
      Width = image.Width,
      Height = image.Height,
      NominalSize = Math.Max(image.Width, image.Height),
      PixelData = argb,
    };
  }
}
