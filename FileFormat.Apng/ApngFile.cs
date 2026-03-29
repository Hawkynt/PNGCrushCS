using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Core;
using FileFormat.Png;

namespace FileFormat.Apng;

/// <summary>In-memory representation of an Animated PNG file.</summary>
public sealed class ApngFile : IImageFileFormat<ApngFile>, IMultiImageFileFormat<ApngFile> {

  static string IImageFileFormat<ApngFile>.PrimaryExtension => ".apng";
  static string[] IImageFileFormat<ApngFile>.FileExtensions => [".apng"];
  static FormatCapability IImageFileFormat<ApngFile>.Capabilities => FormatCapability.VariableResolution | FormatCapability.MultiImage;
  static ApngFile IImageFileFormat<ApngFile>.FromFile(FileInfo file) => ApngReader.FromFile(file);
  static ApngFile IImageFileFormat<ApngFile>.FromBytes(byte[] data) => ApngReader.FromBytes(data);
  static ApngFile IImageFileFormat<ApngFile>.FromStream(Stream stream) => ApngReader.FromStream(stream);
  static byte[] IImageFileFormat<ApngFile>.ToBytes(ApngFile file) => ApngWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int BitDepth { get; init; }
  public PngColorType ColorType { get; init; }
  public int NumPlays { get; init; }
  public IReadOnlyList<ApngFrame> Frames { get; init; } = [];
  public byte[]? Palette { get; init; }
  public byte[]? Transparency { get; init; }

  /// <summary>Returns the number of frames in this APNG file.</summary>
  public static int ImageCount(ApngFile file) => file.Frames.Count;

  /// <summary>Converts the frame at the given index to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(ApngFile file, int index) {
    ArgumentNullException.ThrowIfNull(file);
    if ((uint)index >= (uint)file.Frames.Count)
      throw new ArgumentOutOfRangeException(nameof(index));

    return _FrameToRawImage(file, file.Frames[index]);
  }

  /// <summary>Converts the first frame of an APNG file to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(ApngFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.Frames.Count == 0)
      throw new ArgumentException("APNG file contains no frames.", nameof(file));

    return _FrameToRawImage(file, file.Frames[0]);
  }

  private static RawImage _FrameToRawImage(ApngFile file, ApngFrame frame) {
    var canvasWidth = file.Width;
    var canvasHeight = file.Height;
    var bitDepth = file.BitDepth;

    var bytesPerPixel = _BytesPerPixel(file.ColorType, bitDepth);
    var frameRowBytes = frame.Width * bytesPerPixel;
    var isSubRegion = frame.XOffset != 0 || frame.YOffset != 0 || frame.Width != canvasWidth || frame.Height != canvasHeight;

    byte[] flatPixels;
    if (isSubRegion) {
      var canvasRowBytes = canvasWidth * bytesPerPixel;
      flatPixels = new byte[canvasRowBytes * canvasHeight];
      for (var y = 0; y < frame.Height && frame.YOffset + y < canvasHeight; ++y) {
        var src = frame.PixelData[y];
        var dstOffset = (frame.YOffset + y) * canvasRowBytes + frame.XOffset * bytesPerPixel;
        var copyLen = Math.Min(src.Length, canvasRowBytes - frame.XOffset * bytesPerPixel);
        if (copyLen > 0)
          src.AsSpan(0, copyLen).CopyTo(flatPixels.AsSpan(dstOffset));
      }
    } else {
      flatPixels = new byte[frameRowBytes * frame.Height];
      for (var y = 0; y < frame.Height; ++y)
        frame.PixelData[y].AsSpan(0, frameRowBytes).CopyTo(flatPixels.AsSpan(y * frameRowBytes));
    }

    return file.ColorType switch {
      PngColorType.Palette => _IndexedToRawImage(file, canvasWidth, canvasHeight, flatPixels),
      PngColorType.RGB => new RawImage {
        Width = canvasWidth,
        Height = canvasHeight,
        Format = PixelFormat.Rgb24,
        PixelData = flatPixels,
      },
      PngColorType.RGBA => new RawImage {
        Width = canvasWidth,
        Height = canvasHeight,
        Format = PixelFormat.Rgba32,
        PixelData = flatPixels,
      },
      PngColorType.Grayscale => _GrayscaleToRawImage(canvasWidth, canvasHeight, bitDepth, flatPixels),
      PngColorType.GrayscaleAlpha => _GrayscaleAlphaToRawImage(canvasWidth, canvasHeight, flatPixels),
      _ => throw new NotSupportedException($"Unsupported APNG color type: {file.ColorType}")
    };
  }

  /// <summary>Creates a single-frame APNG from a <see cref="RawImage"/>.</summary>
  public static ApngFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    PngColorType colorType;
    int bitDepth = 8;
    byte[] flatPixels;

    switch (image.Format) {
      case PixelFormat.Rgba32:
        colorType = PngColorType.RGBA;
        flatPixels = image.PixelData;
        break;
      case PixelFormat.Rgb24:
        colorType = PngColorType.RGB;
        flatPixels = image.PixelData;
        break;
      case PixelFormat.Indexed8 when image.Palette != null:
        colorType = PngColorType.Palette;
        flatPixels = image.PixelData;
        break;
      default: {
        // Convert to RGBA32 via PixelConverter
        var converted = PixelConverter.Convert(image, PixelFormat.Rgba32);
        colorType = PngColorType.RGBA;
        flatPixels = converted.PixelData;
        break;
      }
    }

    var width = image.Width;
    var height = image.Height;
    var bytesPerPixel = _BytesPerPixel(colorType, bitDepth);
    var rowBytes = width * bytesPerPixel;

    // Split flat pixels into per-row arrays
    var pixelData = new byte[height][];
    for (var y = 0; y < height; ++y) {
      pixelData[y] = new byte[rowBytes];
      flatPixels.AsSpan(y * rowBytes, rowBytes).CopyTo(pixelData[y].AsSpan(0));
    }

    var frame = new ApngFrame {
      Width = width,
      Height = height,
      XOffset = 0,
      YOffset = 0,
      DelayNumerator = 0,
      DelayDenominator = 1,
      DisposeOp = ApngDisposeOp.None,
      BlendOp = ApngBlendOp.Source,
      PixelData = pixelData,
    };

    return new() {
      Width = width,
      Height = height,
      BitDepth = bitDepth,
      ColorType = colorType,
      NumPlays = 0,
      Frames = [frame],
      Palette = colorType == PngColorType.Palette ? image.Palette : null,
      Transparency = colorType == PngColorType.Palette ? image.AlphaTable : null,
    };
  }

  private static int _BytesPerPixel(PngColorType colorType, int bitDepth) => colorType switch {
    PngColorType.Grayscale => Math.Max(1, bitDepth / 8),
    PngColorType.RGB => 3 * (bitDepth / 8),
    PngColorType.Palette => 1,
    PngColorType.GrayscaleAlpha => 2 * (bitDepth / 8),
    PngColorType.RGBA => 4 * (bitDepth / 8),
    _ => throw new NotSupportedException($"Unknown PNG color type: {colorType}")
  };

  private static RawImage _IndexedToRawImage(ApngFile file, int width, int height, byte[] pixels) {
    byte[]? palette = null;
    var paletteCount = 0;
    if (file.Palette != null) {
      paletteCount = file.Palette.Length / 3;
      palette = file.Palette[..];
    }

    return new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = pixels,
      Palette = palette,
      PaletteCount = paletteCount,
      AlphaTable = file.Transparency != null ? file.Transparency[..] : null,
    };
  }

  private static RawImage _GrayscaleToRawImage(int width, int height, int bitDepth, byte[] pixels) {
    if (bitDepth == 8) {
      var palette = new byte[256 * 3];
      for (var i = 0; i < 256; ++i) {
        var v = (byte)i;
        palette[i * 3] = v;
        palette[i * 3 + 1] = v;
        palette[i * 3 + 2] = v;
      }

      return new RawImage {
        Width = width,
        Height = height,
        Format = PixelFormat.Indexed8,
        PixelData = pixels,
        Palette = palette,
        PaletteCount = 256,
      };
    }

    if (bitDepth == 16) {
      var rgb = new byte[width * height * 3];
      for (var i = 0; i < width * height; ++i) {
        var v = pixels[i * 2]; // high byte
        rgb[i * 3] = v;
        rgb[i * 3 + 1] = v;
        rgb[i * 3 + 2] = v;
      }

      return new RawImage {
        Width = width,
        Height = height,
        Format = PixelFormat.Rgb24,
        PixelData = rgb,
      };
    }

    // Sub-byte grayscale: expand to 8-bit indexed
    var maxVal = (1 << bitDepth) - 1;
    var expanded = new byte[width * height];
    var bitIndex = 0;
    for (var i = 0; i < width * height; ++i) {
      var byteIdx = bitIndex / 8;
      var bitOff = 8 - bitDepth - (bitIndex % 8);
      expanded[i] = (byte)(((pixels[byteIdx] >> bitOff) & maxVal) * 255 / maxVal);
      bitIndex += bitDepth;
    }

    var grayPalette = new byte[256 * 3];
    for (var i = 0; i < 256; ++i) {
      grayPalette[i * 3] = (byte)i;
      grayPalette[i * 3 + 1] = (byte)i;
      grayPalette[i * 3 + 2] = (byte)i;
    }

    return new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Indexed8,
      PixelData = expanded,
      Palette = grayPalette,
      PaletteCount = 256,
    };
  }

  private static RawImage _GrayscaleAlphaToRawImage(int width, int height, byte[] pixels) {
    var rgba = new byte[width * height * 4];
    for (var i = 0; i < width * height; ++i) {
      var gray = pixels[i * 2];
      var alpha = pixels[i * 2 + 1];
      rgba[i * 4] = gray;
      rgba[i * 4 + 1] = gray;
      rgba[i * 4 + 2] = gray;
      rgba[i * 4 + 3] = alpha;
    }

    return new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgba32,
      PixelData = rgba,
    };
  }
}
