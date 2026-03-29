using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.SunRaster;

/// <summary>In-memory representation of a Sun Raster image.</summary>
[FormatMagicBytes([0x59, 0xA6, 0x6A, 0x95])]
public sealed class SunRasterFile : IImageFileFormat<SunRasterFile> {

  static string IImageFileFormat<SunRasterFile>.PrimaryExtension => ".ras";
  static string[] IImageFileFormat<SunRasterFile>.FileExtensions => [".ras", ".sun", ".rast", ".rs"];
  static SunRasterFile IImageFileFormat<SunRasterFile>.FromFile(FileInfo file) => SunRasterReader.FromFile(file);
  static SunRasterFile IImageFileFormat<SunRasterFile>.FromBytes(byte[] data) => SunRasterReader.FromBytes(data);
  static SunRasterFile IImageFileFormat<SunRasterFile>.FromStream(Stream stream) => SunRasterReader.FromStream(stream);
  static RawImage IImageFileFormat<SunRasterFile>.ToRawImage(SunRasterFile file) => file.ToRawImage();
  static byte[] IImageFileFormat<SunRasterFile>.ToBytes(SunRasterFile file) => SunRasterWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int Depth { get; init; }
  public SunRasterCompression Compression { get; init; }
  public SunRasterColorMode ColorMode { get; init; }
  public byte[] PixelData { get; init; } = [];
  public byte[]? Palette { get; init; }
  public int PaletteColorCount { get; init; }

  public RawImage ToRawImage() {
    var width = this.Width;
    var height = this.Height;
    switch (this.ColorMode) {
      case SunRasterColorMode.Rgb24: {
        var bytesPerRow = width * 3;
        var paddedBytesPerRow = _PadTo2(bytesPerRow);
        var stripped = _StripRowPadding(this.PixelData, bytesPerRow, paddedBytesPerRow, height);
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Bgr24,
          PixelData = stripped,
        };
      }
      case SunRasterColorMode.Rgb32: {
        // Sun Raster 32-bit: [pad/alpha, B, G, R] per pixel → convert to [B, G, R, A] for Bgra32
        var bytesPerRow = width * 4;
        var paddedBytesPerRow = _PadTo2(bytesPerRow);
        var stripped = _StripRowPadding(this.PixelData, bytesPerRow, paddedBytesPerRow, height);
        var pixels = this.Width * this.Height;
        var result = new byte[pixels * 4];
        for (var i = 0; i < pixels; ++i) {
          var src = i * 4;
          var dst = i * 4;
          result[dst]     = stripped[src + 1]; // B
          result[dst + 1] = stripped[src + 2]; // G
          result[dst + 2] = stripped[src + 3]; // R
          result[dst + 3] = stripped[src];     // A (was pad byte)
        }
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Bgra32,
          PixelData = result,
        };
      }
      case SunRasterColorMode.Palette8: {
        var bytesPerRow = width;
        var paddedBytesPerRow = _PadTo2(bytesPerRow);
        var stripped = _StripRowPadding(this.PixelData, bytesPerRow, paddedBytesPerRow, height);
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Indexed8,
          PixelData = stripped,
          Palette = this.Palette,
          PaletteCount = this.PaletteColorCount,
        };
      }
      case SunRasterColorMode.Monochrome: {
        var bytesPerRow = (width + 7) / 8;
        var paddedBytesPerRow = _PadTo2(bytesPerRow);
        var stripped = _StripRowPadding(this.PixelData, bytesPerRow, paddedBytesPerRow, height);
        // B/W palette: index 0 = white (255,255,255), index 1 = black (0,0,0)
        // In Sun Raster 1bpp, bit=1 means black, bit=0 means white (standard)
        var palette = new byte[] { 255, 255, 255, 0, 0, 0 };
        return new() {
          Width = width,
          Height = height,
          Format = PixelFormat.Indexed1,
          PixelData = stripped,
          Palette = palette,
          PaletteCount = 2,
        };
      }
      case SunRasterColorMode.Original:
      default: {
        switch (this.Depth) {
          case 1: {
            var bytesPerRow = (width + 7) / 8;
            var paddedBytesPerRow = _PadTo2(bytesPerRow);
            var stripped = _StripRowPadding(this.PixelData, bytesPerRow, paddedBytesPerRow, height);
            var palette = new byte[] { 255, 255, 255, 0, 0, 0 };
            return new() {
              Width = width,
              Height = height,
              Format = PixelFormat.Indexed1,
              PixelData = stripped,
              Palette = palette,
              PaletteCount = 2,
            };
          }
          case 8: {
            var bytesPerRow = width;
            var paddedBytesPerRow = _PadTo2(bytesPerRow);
            var stripped = _StripRowPadding(this.PixelData, bytesPerRow, paddedBytesPerRow, height);
            if (this.Palette != null && this.PaletteColorCount > 0)
              return new() {
                Width = width,
                Height = height,
                Format = PixelFormat.Indexed8,
                PixelData = stripped,
                Palette = this.Palette,
                PaletteCount = this.PaletteColorCount,
              };
            return new() {
              Width = width,
              Height = height,
              Format = PixelFormat.Gray8,
              PixelData = stripped,
            };
          }
          case 24: {
            var bytesPerRow = width * 3;
            var paddedBytesPerRow = _PadTo2(bytesPerRow);
            var stripped = _StripRowPadding(this.PixelData, bytesPerRow, paddedBytesPerRow, height);
            return new() {
              Width = width,
              Height = height,
              Format = PixelFormat.Bgr24,
              PixelData = stripped,
            };
          }
          case 32: {
            var bytesPerRow = width * 4;
            var paddedBytesPerRow = _PadTo2(bytesPerRow);
            var stripped = _StripRowPadding(this.PixelData, bytesPerRow, paddedBytesPerRow, height);
            var pixels = width * height;
            var result = new byte[pixels * 4];
            for (var i = 0; i < pixels; ++i) {
              var src = i * 4;
              var dst = i * 4;
              result[dst]     = stripped[src + 1]; // B
              result[dst + 1] = stripped[src + 2]; // G
              result[dst + 2] = stripped[src + 3]; // R
              result[dst + 3] = stripped[src];     // A
            }
            return new() {
              Width = width,
              Height = height,
              Format = PixelFormat.Bgra32,
              PixelData = result,
            };
          }
          default:
            throw new NotSupportedException($"Sun Raster depth {this.Depth} is not supported.");
        }
      }
    }
  }

  public static SunRasterFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var width = image.Width;
    var height = image.Height;
    switch (image.Format) {
      case PixelFormat.Bgr24: {
        var bytesPerRow = width * 3;
        var paddedBytesPerRow = _PadTo2(bytesPerRow);
        var padded = _AddRowPadding(image.PixelData, bytesPerRow, paddedBytesPerRow, height);
        return new() {
          Width = width,
          Height = height,
          Depth = 24,
          Compression = SunRasterCompression.None,
          ColorMode = SunRasterColorMode.Rgb24,
          PixelData = padded,
        };
      }
      case PixelFormat.Bgra32: {
        // Convert [B,G,R,A] → [A,B,G,R] for Sun Raster 32-bit layout
        var pixels = width * height;
        var converted = new byte[pixels * 4];
        for (var i = 0; i < pixels; ++i) {
          var src = i * 4;
          var dst = i * 4;
          converted[dst]     = image.PixelData[src + 3]; // A → pad/alpha byte
          converted[dst + 1] = image.PixelData[src];     // B
          converted[dst + 2] = image.PixelData[src + 1]; // G
          converted[dst + 3] = image.PixelData[src + 2]; // R
        }
        var bytesPerRow = width * 4;
        var paddedBytesPerRow = _PadTo2(bytesPerRow);
        var padded = _AddRowPadding(converted, bytesPerRow, paddedBytesPerRow, height);
        return new() {
          Width = width,
          Height = height,
          Depth = 32,
          Compression = SunRasterCompression.None,
          ColorMode = SunRasterColorMode.Rgb32,
          PixelData = padded,
        };
      }
      case PixelFormat.Indexed8: {
        var bytesPerRow = width;
        var paddedBytesPerRow = _PadTo2(bytesPerRow);
        var padded = _AddRowPadding(image.PixelData, bytesPerRow, paddedBytesPerRow, height);
        return new() {
          Width = width,
          Height = height,
          Depth = 8,
          Compression = SunRasterCompression.None,
          ColorMode = SunRasterColorMode.Palette8,
          PixelData = padded,
          Palette = image.Palette,
          PaletteColorCount = image.PaletteCount,
        };
      }
      case PixelFormat.Indexed1: {
        var bytesPerRow = (width + 7) / 8;
        var paddedBytesPerRow = _PadTo2(bytesPerRow);
        var padded = _AddRowPadding(image.PixelData, bytesPerRow, paddedBytesPerRow, height);
        return new() {
          Width = width,
          Height = height,
          Depth = 1,
          Compression = SunRasterCompression.None,
          ColorMode = SunRasterColorMode.Monochrome,
          PixelData = padded,
        };
      }
      case PixelFormat.Gray8: {
        // Treat as Palette8 with grayscale palette (256 entries)
        var palette = new byte[256 * 3];
        for (var i = 0; i < 256; ++i) {
          palette[i * 3]     = (byte)i;
          palette[i * 3 + 1] = (byte)i;
          palette[i * 3 + 2] = (byte)i;
        }
        var bytesPerRow = width;
        var paddedBytesPerRow = _PadTo2(bytesPerRow);
        var padded = _AddRowPadding(image.PixelData, bytesPerRow, paddedBytesPerRow, height);
        return new() {
          Width = width,
          Height = height,
          Depth = 8,
          Compression = SunRasterCompression.None,
          ColorMode = SunRasterColorMode.Palette8,
          PixelData = padded,
          Palette = palette,
          PaletteColorCount = 256,
        };
      }
      default:
        throw new ArgumentException($"Pixel format {image.Format} is not supported by Sun Raster.", nameof(image));
    }
  }

  private static byte[] _AddRowPadding(byte[] data, int bytesPerRow, int paddedBytesPerRow, int height) {
    if (bytesPerRow == paddedBytesPerRow)
      return data[..];
    var result = new byte[paddedBytesPerRow * height];
    for (var y = 0; y < height; ++y)
      data.AsSpan(y * bytesPerRow, bytesPerRow).CopyTo(result.AsSpan(y * paddedBytesPerRow));
    return result;
  }

  private static byte[] _StripRowPadding(byte[] data, int bytesPerRow, int paddedBytesPerRow, int height) {
    if (bytesPerRow == paddedBytesPerRow)
      return data[..];
    var result = new byte[bytesPerRow * height];
    for (var y = 0; y < height; ++y)
      data.AsSpan(y * paddedBytesPerRow, bytesPerRow).CopyTo(result.AsSpan(y * bytesPerRow));
    return result;
  }

  private static int _PadTo2(int value) => (value + 1) & ~1;
}
