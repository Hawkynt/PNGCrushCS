using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Bmp;
using FileFormat.Core;
using FileFormat.Png;

namespace FileFormat.Ico;

/// <summary>In-memory representation of an ICO file.</summary>
[FormatMagicBytes([0x00, 0x00, 0x01, 0x00])]
public sealed class IcoFile : IImageFormatReader<IcoFile>, IImageToRawImage<IcoFile>, IImageFormatWriter<IcoFile>, IMultiImageFileFormat<IcoFile> {

  static string IImageFormatMetadata<IcoFile>.PrimaryExtension => ".ico";
  static string[] IImageFormatMetadata<IcoFile>.FileExtensions => [".ico"];
  static IcoFile IImageFormatReader<IcoFile>.FromSpan(ReadOnlySpan<byte> data) => IcoReader.FromSpan(data);
  static FormatCapability IImageFormatMetadata<IcoFile>.Capabilities => FormatCapability.HasDedicatedOptimizer | FormatCapability.MultiImage;
  static byte[] IImageFormatWriter<IcoFile>.ToBytes(IcoFile file) => IcoWriter.ToBytes(file);
  public IReadOnlyList<IcoImage> Images { get; init; } = [];

  /// <summary>Returns the number of image entries in this ICO file.</summary>
  public static int ImageCount(IcoFile file) => file.Images.Count;

  /// <summary>Converts the image entry at the given index to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(IcoFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.Images.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    var entry = file.Images[index];
    return entry.Format == IcoImageFormat.Png
      ? PngFile.ToRawImage(PngReader.FromBytes(entry.Data))
      : _DecodeDib(entry);
  }

  /// <summary>Converts the largest image entry of an ICO file to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(IcoFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Images.Count == 0)
      throw new ArgumentException("ICO file contains no images.", nameof(file));

    var best = file.Images
      .OrderByDescending(i => i.Width * i.Height)
      .ThenByDescending(i => i.BitsPerPixel)
      .First();

    return best.Format == IcoImageFormat.Png
      ? PngFile.ToRawImage(PngReader.FromBytes(best.Data))
      : _DecodeDib(best);
  }

  private static RawImage _DecodeDib(IcoImage entry) {
    var dib = entry.Data;
    if (dib.Length < 40)
      throw new InvalidOperationException("BMP DIB data too small for BITMAPINFOHEADER.");

    var biSize = BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(0));
    var biBitCount = BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(14));
    var width = entry.Width;
    var height = entry.Height;

    switch (biBitCount) {
      case 32: {
        var dataOffset = biSize;
        var stride = width * 4;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; ++y)
          dib.AsSpan(dataOffset + (height - 1 - y) * stride, stride).CopyTo(pixels.AsSpan(y * stride));

        return new RawImage {
          Width = width,
          Height = height,
          Format = PixelFormat.Bgra32,
          PixelData = pixels,
        };
      }
      case 24: {
        var dataOffset = biSize;
        var srcStride = ((width * 3 + 3) / 4) * 4;
        var dstStride = width * 3;
        var pixels = new byte[dstStride * height];
        for (var y = 0; y < height; ++y)
          dib.AsSpan(dataOffset + (height - 1 - y) * srcStride, dstStride).CopyTo(pixels.AsSpan(y * dstStride));

        return new RawImage {
          Width = width,
          Height = height,
          Format = PixelFormat.Bgr24,
          PixelData = pixels,
        };
      }
      case 8: {
        var paletteCount = 256;
        var paletteOffset = biSize;
        var palette = new byte[paletteCount * 3];
        for (var i = 0; i < paletteCount && paletteOffset + i * 4 + 2 < dib.Length; ++i) {
          var off = paletteOffset + i * 4;
          palette[i * 3] = dib[off + 2];     // R
          palette[i * 3 + 1] = dib[off + 1]; // G
          palette[i * 3 + 2] = dib[off];     // B
        }

        var dataOffset = biSize + paletteCount * 4;
        var srcStride = ((width + 3) / 4) * 4;
        var pixels = new byte[width * height];
        for (var y = 0; y < height; ++y)
          dib.AsSpan(dataOffset + (height - 1 - y) * srcStride, width).CopyTo(pixels.AsSpan(y * width));

        return new RawImage {
          Width = width,
          Height = height,
          Format = PixelFormat.Indexed8,
          PixelData = pixels,
          Palette = palette,
          PaletteCount = paletteCount,
        };
      }
      case 4: {
        var biClrUsed = BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(32));
        var paletteCount = biClrUsed > 0 ? biClrUsed : 16;
        var paletteOffset = biSize;
        var palette = new byte[paletteCount * 3];
        for (var i = 0; i < paletteCount && paletteOffset + i * 4 + 2 < dib.Length; ++i) {
          var off = paletteOffset + i * 4;
          palette[i * 3] = dib[off + 2];
          palette[i * 3 + 1] = dib[off + 1];
          palette[i * 3 + 2] = dib[off];
        }

        var dataOffset = biSize + paletteCount * 4;
        var srcStride = ((width * 4 + 31) / 32) * 4;
        var packed = new byte[((width + 1) / 2) * height];
        var dstStride = (width + 1) / 2;
        for (var y = 0; y < height; ++y)
          dib.AsSpan(dataOffset + (height - 1 - y) * srcStride, dstStride).CopyTo(packed.AsSpan(y * dstStride));

        return new RawImage {
          Width = width,
          Height = height,
          Format = PixelFormat.Indexed4,
          PixelData = packed,
          Palette = palette,
          PaletteCount = paletteCount,
        };
      }
      default:
        throw new NotSupportedException($"ICO BMP DIB with {biBitCount} bits per pixel is not supported.");
    }
  }
}
