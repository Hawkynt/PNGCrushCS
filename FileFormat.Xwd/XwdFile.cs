using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Xwd;

/// <summary>In-memory representation of an XWD (X Window Dump) image.</summary>
public sealed class XwdFile : IImageFileFormat<XwdFile> {

  static string IImageFileFormat<XwdFile>.PrimaryExtension => ".xwd";
  static string[] IImageFileFormat<XwdFile>.FileExtensions => [".xwd"];
  static XwdFile IImageFileFormat<XwdFile>.FromFile(FileInfo file) => XwdReader.FromFile(file);
  static XwdFile IImageFileFormat<XwdFile>.FromBytes(byte[] data) => XwdReader.FromBytes(data);
  static XwdFile IImageFileFormat<XwdFile>.FromStream(Stream stream) => XwdReader.FromStream(stream);
  static byte[] IImageFileFormat<XwdFile>.ToBytes(XwdFile file) => XwdWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int BitsPerPixel { get; init; }
  public int BytesPerLine { get; init; }
  public XwdPixmapFormat PixmapFormat { get; init; }
  public int PixmapDepth { get; init; }
  public XwdVisualClass VisualClass { get; init; }
  public uint ByteOrder { get; init; }
  public uint BitmapUnit { get; init; }
  public uint BitmapBitOrder { get; init; }
  public uint BitmapPad { get; init; }
  public uint XOffset { get; init; }
  public uint BitsPerRgb { get; init; }
  public uint ColormapEntries { get; init; }
  public uint RedMask { get; init; }
  public uint GreenMask { get; init; }
  public uint BlueMask { get; init; }
  public int WindowX { get; init; }
  public int WindowY { get; init; }
  public uint WindowBorderWidth { get; init; }
  public string WindowName { get; init; } = "";
  public byte[] PixelData { get; init; } = [];

  /// <summary>Raw colormap data: 12 bytes per entry (pixel u32 BE, red u16 BE, green u16 BE, blue u16 BE, flags u8, padding u8).</summary>
  public byte[]? Colormap { get; init; }

  public static RawImage ToRawImage(XwdFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var width = file.Width;
    var height = file.Height;
    var bytesPerLine = file.BytesPerLine;

    switch (file.BitsPerPixel) {
      case 32: {
        var rowBytes = width * 4;
        var pixels = _StripRowPadding(file.PixelData, bytesPerLine, rowBytes, height);
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Bgra32,
          PixelData = pixels,
        };
      }
      case 24: {
        var rowBytes = width * 3;
        var pixels = _StripRowPadding(file.PixelData, bytesPerLine, rowBytes, height);
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Rgb24,
          PixelData = pixels,
        };
      }
      case 8 when file.Colormap != null: {
        var rowBytes = width;
        var pixels = _StripRowPadding(file.PixelData, bytesPerLine, rowBytes, height);
        var palette = _ExtractPalette(file.Colormap);
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Indexed8,
          PixelData = pixels,
          Palette = palette,
          PaletteCount = palette.Length / 3,
        };
      }
      default:
        throw new NotSupportedException($"XWD with {file.BitsPerPixel} bits per pixel is not supported.");
    }
  }

  public static XwdFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var width = image.Width;
    var height = image.Height;

    switch (image.Format) {
      case PixelFormat.Bgra32:
        return new() {
          Width = width,
          Height = height,
          BitsPerPixel = 32,
          BytesPerLine = width * 4,
          PixmapFormat = XwdPixmapFormat.ZPixmap,
          PixmapDepth = 32,
          VisualClass = XwdVisualClass.TrueColor,
          BitsPerRgb = 8,
          PixelData = image.PixelData[..],
        };
      case PixelFormat.Rgb24:
        return new() {
          Width = width,
          Height = height,
          BitsPerPixel = 24,
          BytesPerLine = width * 3,
          PixmapFormat = XwdPixmapFormat.ZPixmap,
          PixmapDepth = 24,
          VisualClass = XwdVisualClass.TrueColor,
          BitsPerRgb = 8,
          PixelData = image.PixelData[..],
        };
      default:
        throw new ArgumentException($"Pixel format {image.Format} is not supported by XWD.", nameof(image));
    }
  }

  private static byte[] _StripRowPadding(byte[] data, int srcStride, int dstStride, int height) {
    if (srcStride == dstStride)
      return data[..];

    var result = new byte[dstStride * height];
    for (var y = 0; y < height; ++y)
      data.AsSpan(y * srcStride, dstStride).CopyTo(result.AsSpan(y * dstStride));
    return result;
  }

  /// <summary>Extracts an RGB palette from XWD colormap data (12 bytes per entry: u32 pixel, u16 red, u16 green, u16 blue, u8 flags, u8 pad).</summary>
  private static byte[] _ExtractPalette(byte[] colormap) {
    var entryCount = colormap.Length / 12;
    var palette = new byte[entryCount * 3];
    for (var i = 0; i < entryCount; ++i) {
      var offset = i * 12;
      palette[i * 3]     = colormap[offset + 4]; // high byte of red u16 BE
      palette[i * 3 + 1] = colormap[offset + 6]; // high byte of green u16 BE
      palette[i * 3 + 2] = colormap[offset + 8]; // high byte of blue u16 BE
    }
    return palette;
  }
}
