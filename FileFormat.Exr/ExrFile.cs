using System;
using System.Collections.Generic;
using System.Linq;
using FileFormat.Core;

namespace FileFormat.Exr;

/// <summary>In-memory representation of an OpenEXR image.</summary>
[FormatMagicBytes([0x76, 0x2F, 0x31, 0x01])]
public readonly record struct ExrFile : IImageFormatReader<ExrFile>, IImageToRawImage<ExrFile>, IImageFromRawImage<ExrFile>, IImageFormatWriter<ExrFile> {

  static string IImageFormatMetadata<ExrFile>.PrimaryExtension => ".exr";
  static string[] IImageFormatMetadata<ExrFile>.FileExtensions => [".exr"];
  static ExrFile IImageFormatReader<ExrFile>.FromSpan(ReadOnlySpan<byte> data) => ExrReader.FromSpan(data);
  static byte[] IImageFormatWriter<ExrFile>.ToBytes(ExrFile file) => ExrWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public ExrCompression Compression { get; init; }
  public ExrLineOrder LineOrder { get; init; }
  public IReadOnlyList<ExrChannel> Channels { get; init; }
  public byte[] PixelData { get; init; }
  public IReadOnlyList<ExrAttribute>? Attributes { get; init; }

  /// <summary>Converts this EXR image to a 16-bit <see cref="RawImage"/>. Outputs Gray16 (single channel), Rgba64 (with alpha), or Rgb48.</summary>
  public static RawImage ToRawImage(ExrFile file) {
    var width = file.Width;
    var height = file.Height;
    var src = file.PixelData;
    var channels = file.Channels.OrderBy(c => c.Name, StringComparer.Ordinal).ToArray();
    var channelCount = channels.Length;

    var bytesPerSample = new int[channelCount];
    for (var i = 0; i < channelCount; ++i)
      bytesPerSample[i] = channels[i].PixelType switch {
        ExrPixelType.Half => 2,
        ExrPixelType.Float => 4,
        ExrPixelType.UInt => 4,
        _ => throw new NotSupportedException($"EXR pixel type {channels[i].PixelType} is not supported.")
      };

    var scanlineStride = 0;
    var channelOffsetInScanline = new int[channelCount];
    for (var i = 0; i < channelCount; ++i) {
      channelOffsetInScanline[i] = scanlineStride;
      scanlineStride += width * bytesPerSample[i];
    }

    var rIdx = -1;
    var gIdx = -1;
    var bIdx = -1;
    var aIdx = -1;
    for (var i = 0; i < channelCount; ++i)
      switch (channels[i].Name) {
        case "R": rIdx = i; break;
        case "G": gIdx = i; break;
        case "B": bIdx = i; break;
        case "A": aIdx = i; break;
      }

    // Single-channel grayscale (e.g., luminance-only EXR)
    if (channelCount == 1 && rIdx < 0 && gIdx < 0 && bIdx < 0) {
      var pixelCount = width * height;
      var result = new byte[pixelCount * 2];
      for (var y = 0; y < height; ++y) {
        var scanBase = y * scanlineStride;
        for (var x = 0; x < width; ++x) {
          var offset = scanBase + x * bytesPerSample[0];
          var val = _ReadFloat(src, offset, channels[0].PixelType);
          var u16 = _FloatToUInt16(val);
          var di = (y * width + x) * 2;
          result[di] = (byte)(u16 >> 8);
          result[di + 1] = (byte)u16;
        }
      }

      return new() {
        Width = width,
        Height = height,
        Format = PixelFormat.Gray16,
        PixelData = result,
      };
    }

    if (rIdx < 0 || gIdx < 0 || bIdx < 0)
      throw new NotSupportedException("EXR image does not contain R, G, B channels.");

    var hasAlpha = aIdx >= 0;
    var bpp = hasAlpha ? 8 : 6;
    var pixels = new byte[width * height * bpp];

    for (var y = 0; y < height; ++y) {
      var scanBase = y * scanlineStride;
      for (var x = 0; x < width; ++x) {
        var rOff = scanBase + channelOffsetInScanline[rIdx] + x * bytesPerSample[rIdx];
        var gOff = scanBase + channelOffsetInScanline[gIdx] + x * bytesPerSample[gIdx];
        var bOff = scanBase + channelOffsetInScanline[bIdx] + x * bytesPerSample[bIdx];

        var r = _FloatToUInt16(_ReadFloat(src, rOff, channels[rIdx].PixelType));
        var g = _FloatToUInt16(_ReadFloat(src, gOff, channels[gIdx].PixelType));
        var b = _FloatToUInt16(_ReadFloat(src, bOff, channels[bIdx].PixelType));

        var di = (y * width + x) * bpp;
        pixels[di] = (byte)(r >> 8);
        pixels[di + 1] = (byte)r;
        pixels[di + 2] = (byte)(g >> 8);
        pixels[di + 3] = (byte)g;
        pixels[di + 4] = (byte)(b >> 8);
        pixels[di + 5] = (byte)b;

        if (hasAlpha) {
          var aOff = scanBase + channelOffsetInScanline[aIdx] + x * bytesPerSample[aIdx];
          var a = _FloatToUInt16(_ReadFloat(src, aOff, channels[aIdx].PixelType));
          pixels[di + 6] = (byte)(a >> 8);
          pixels[di + 7] = (byte)a;
        }
      }
    }

    return new() {
      Width = width,
      Height = height,
      Format = hasAlpha ? PixelFormat.Rgba64 : PixelFormat.Rgb48,
      PixelData = pixels,
    };
  }

  /// <summary>Creates an EXR Half-float image from a <see cref="RawImage"/>. Accepts Rgba64/Rgb48 natively or any convertible format.</summary>
  public static ExrFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var hasAlpha = image.HasAlpha;
    var rgba64 = hasAlpha
      ? PixelConverter.Convert(image, PixelFormat.Rgba64)
      : PixelConverter.Convert(image, PixelFormat.Rgb48);
    var width = rgba64.Width;
    var height = rgba64.Height;
    var src = rgba64.PixelData;
    var pixelCount = width * height;
    var srcBpp = hasAlpha ? 8 : 6;

    // Build channels in alphabetical order (A, B, G, R) as EXR spec requires
    var channelNames = hasAlpha ? new[] { "A", "B", "G", "R" } : new[] { "B", "G", "R" };
    var channelCount = channelNames.Length;
    var channels = new ExrChannel[channelCount];
    for (var i = 0; i < channelCount; ++i)
      channels[i] = new() { Name = channelNames[i], PixelType = ExrPixelType.Half };

    // Per-scanline interleaved channels: each scanline has all channels in order
    var bytesPerSample = 2; // Half = 2 bytes
    var scanlineStride = channelCount * width * bytesPerSample;
    var pixelData = new byte[height * scanlineStride];

    for (var y = 0; y < height; ++y) {
      var scanBase = y * scanlineStride;
      for (var ch = 0; ch < channelCount; ++ch) {
        var chOffset = scanBase + ch * width * bytesPerSample;
        for (var x = 0; x < width; ++x) {
          var srcIdx = (y * width + x) * srcBpp;
          // Rgb48: Rhi,Rlo,Ghi,Glo,Bhi,Blo; Rgba64: +Ahi,Alo
          int u16;
          switch (channelNames[ch]) {
            case "R": u16 = (src[srcIdx] << 8) | src[srcIdx + 1]; break;
            case "G": u16 = (src[srcIdx + 2] << 8) | src[srcIdx + 3]; break;
            case "B": u16 = (src[srcIdx + 4] << 8) | src[srcIdx + 5]; break;
            case "A": u16 = hasAlpha ? (src[srcIdx + 6] << 8) | src[srcIdx + 7] : 65535; break;
            default: u16 = 0; break;
          }

          var half = (Half)(u16 / 65535.0f);
          var dstIdx = chOffset + x * bytesPerSample;
          BitConverter.TryWriteBytes(pixelData.AsSpan(dstIdx, 2), half);
        }
      }
    }

    return new() {
      Width = width,
      Height = height,
      Compression = ExrCompression.None,
      LineOrder = ExrLineOrder.IncreasingY,
      Channels = channels,
      PixelData = pixelData,
    };
  }

  private static float _ReadFloat(byte[] data, int offset, ExrPixelType type) => type switch {
    ExrPixelType.Half => (float)BitConverter.ToHalf(data, offset),
    ExrPixelType.Float => BitConverter.ToSingle(data, offset),
    ExrPixelType.UInt => BitConverter.ToUInt32(data, offset) / (float)uint.MaxValue,
    _ => throw new NotSupportedException($"EXR pixel type {type} is not supported.")
  };

  private static ushort _FloatToUInt16(float v) => (ushort)Math.Clamp(Math.Max(v, 0f) * 65535f, 0, 65535);
}
