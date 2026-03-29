using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Clp;

/// <summary>In-memory representation of a CLP (Windows Clipboard) image file.</summary>
[FormatMagicBytes([0xC3, 0x50])]
public sealed class ClpFile : IImageFileFormat<ClpFile> {

  static string IImageFileFormat<ClpFile>.PrimaryExtension => ".clp";
  static string[] IImageFileFormat<ClpFile>.FileExtensions => [".clp"];
  static ClpFile IImageFileFormat<ClpFile>.FromFile(FileInfo file) => ClpReader.FromFile(file);
  static ClpFile IImageFileFormat<ClpFile>.FromBytes(byte[] data) => ClpReader.FromBytes(data);
  static ClpFile IImageFileFormat<ClpFile>.FromStream(Stream stream) => ClpReader.FromStream(stream);
  static byte[] IImageFileFormat<ClpFile>.ToBytes(ClpFile file) => ClpWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int BitsPerPixel { get; init; }

  /// <summary>Raw pixel data (bottom-up DIB order, row-padded to 4-byte boundary).</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>BGRX palette (4 bytes per entry), or null if no palette.</summary>
  public byte[]? Palette { get; init; }

  public static RawImage ToRawImage(ClpFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var width = file.Width;
    var height = file.Height;

    switch (file.BitsPerPixel) {
      case 24: {
        var stride = (width * 3 + 3) & ~3;
        var rowBytes = width * 3;
        var pixels = _StripPaddingAndFlip(file.PixelData, stride, rowBytes, height);
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Bgr24,
          PixelData = pixels,
        };
      }
      case 8 when file.Palette != null: {
        var stride = (width + 3) & ~3;
        var pixels = _StripPaddingAndFlip(file.PixelData, stride, width, height);
        var palette = PixelConverter.BgraToRgb(file.Palette, file.Palette.Length / 4);
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Indexed8,
          PixelData = pixels,
          Palette = palette,
          PaletteCount = file.Palette.Length / 4,
        };
      }
      default:
        throw new NotSupportedException($"CLP with {file.BitsPerPixel} bits per pixel is not supported.");
    }
  }

  public static ClpFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var width = image.Width;
    var height = image.Height;

    switch (image.Format) {
      case PixelFormat.Bgr24: {
        var stride = (width * 3 + 3) & ~3;
        var pixels = _AddPaddingAndFlip(image.PixelData, width * 3, stride, height);
        return new() {
          Width = width,
          Height = height,
          BitsPerPixel = 24,
          PixelData = pixels,
        };
      }
      case PixelFormat.Indexed8: {
        var stride = (width + 3) & ~3;
        var pixels = _AddPaddingAndFlip(image.PixelData, width, stride, height);
        var palette = image.Palette != null ? PixelConverter.RgbToBgra(image.Palette, image.Palette.Length / 3) : null;
        return new() {
          Width = width,
          Height = height,
          BitsPerPixel = 8,
          PixelData = pixels,
          Palette = palette,
        };
      }
      default:
        throw new ArgumentException($"Pixel format {image.Format} is not supported by CLP.", nameof(image));
    }
  }

  /// <summary>Strips row padding and flips rows from bottom-up to top-down.</summary>
  private static byte[] _StripPaddingAndFlip(byte[] data, int srcStride, int dstStride, int height) {
    var result = new byte[dstStride * height];
    for (var y = 0; y < height; ++y)
      data.AsSpan((height - 1 - y) * srcStride, dstStride).CopyTo(result.AsSpan(y * dstStride));
    return result;
  }

  /// <summary>Adds row padding and flips rows from top-down to bottom-up.</summary>
  private static byte[] _AddPaddingAndFlip(byte[] data, int srcStride, int dstStride, int height) {
    var result = new byte[dstStride * height];
    for (var y = 0; y < height; ++y)
      data.AsSpan(y * srcStride, srcStride).CopyTo(result.AsSpan((height - 1 - y) * dstStride));
    return result;
  }

}
