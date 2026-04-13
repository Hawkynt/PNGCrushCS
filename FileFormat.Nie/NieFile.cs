using System;
using FileFormat.Core;

namespace FileFormat.Nie;

/// <summary>In-memory representation of a NIE (Wuffs Naive Image) file.</summary>
[FormatMagicBytes([0x6E, 0xC3, 0xAF, 0x45])]
public sealed class NieFile :
  IImageFormatReader<NieFile>, IImageToRawImage<NieFile>,
  IImageFromRawImage<NieFile>, IImageFormatWriter<NieFile> {

  /// <summary>Magic bytes: nïE = 0x6E 0xC3 0xAF 0x45.</summary>
  public static readonly byte[] MagicBytes = [0x6E, 0xC3, 0xAF, 0x45];

  /// <summary>Header size: 4 (magic) + 4 (config + padding) + 4 (width) + 4 (height) = 16 bytes.</summary>
  public const int HeaderSize = 16;

  static string IImageFormatMetadata<NieFile>.PrimaryExtension => ".nie";
  static string[] IImageFormatMetadata<NieFile>.FileExtensions => [".nie"];
  static NieFile IImageFormatReader<NieFile>.FromSpan(ReadOnlySpan<byte> data) => NieReader.FromSpan(data);
  static byte[] IImageFormatWriter<NieFile>.ToBytes(NieFile file) => NieWriter.ToBytes(file);

  public int Width { get; init; }
  public int Height { get; init; }
  public NiePixelConfig PixelConfig { get; init; }

  /// <summary>Raw BGRA pixel data in the configured bit depth.</summary>
  public byte[] PixelData { get; init; } = [];

  /// <summary>Number of bytes per pixel for the current pixel configuration.</summary>
  public int BytesPerPixel => this.PixelConfig is NiePixelConfig.Bgra16 or NiePixelConfig.BgraPremul16 ? 8 : 4;

  /// <summary>Whether the pixel data uses premultiplied alpha.</summary>
  public bool IsPremultiplied => this.PixelConfig is NiePixelConfig.BgraPremul8 or NiePixelConfig.BgraPremul16;

  /// <summary>Whether the pixel data is 16-bit per channel.</summary>
  public bool Is16Bit => this.PixelConfig is NiePixelConfig.Bgra16 or NiePixelConfig.BgraPremul16;

  /// <summary>Converts a NIE image to a <see cref="RawImage"/>.</summary>
  public static RawImage ToRawImage(NieFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var width = file.Width;
    var height = file.Height;
    var src = file.PixelData;
    var pixelCount = width * height;

    if (file.Is16Bit) {
      // BGRA16 → Rgba64 (big-endian uint16 per channel, reorder B,G,R,A → R,G,B,A)
      var result = new byte[pixelCount * 8];
      for (var i = 0; i < pixelCount; ++i) {
        var si = i * 8;
        var di = i * 8;

        // Source: B16-LE, G16-LE, R16-LE, A16-LE
        var b = (ushort)(src[si] | (src[si + 1] << 8));
        var g = (ushort)(src[si + 2] | (src[si + 3] << 8));
        var r = (ushort)(src[si + 4] | (src[si + 5] << 8));
        var a = (ushort)(src[si + 6] | (src[si + 7] << 8));

        if (file.IsPremultiplied && a > 0 && a < 65535) {
          r = (ushort)Math.Min(r * 65535 / a, 65535);
          g = (ushort)Math.Min(g * 65535 / a, 65535);
          b = (ushort)Math.Min(b * 65535 / a, 65535);
        }

        // Dest: R16-BE, G16-BE, B16-BE, A16-BE
        result[di] = (byte)(r >> 8);
        result[di + 1] = (byte)r;
        result[di + 2] = (byte)(g >> 8);
        result[di + 3] = (byte)g;
        result[di + 4] = (byte)(b >> 8);
        result[di + 5] = (byte)b;
        result[di + 6] = (byte)(a >> 8);
        result[di + 7] = (byte)a;
      }

      return new() { Width = width, Height = height, Format = PixelFormat.Rgba64, PixelData = result };
    }

    {
      // BGRA8 → Bgra32 (already in the right channel order for our BGRA32 format)
      var result = new byte[pixelCount * 4];
      var copyLen = Math.Min(src.Length, result.Length);

      if (file.IsPremultiplied) {
        for (var i = 0; i < pixelCount; ++i) {
          var si = i * 4;
          if (si + 3 >= src.Length)
            break;

          var b2 = src[si];
          var g2 = src[si + 1];
          var r2 = src[si + 2];
          var a2 = src[si + 3];

          if (a2 > 0 && a2 < 255) {
            r2 = (byte)Math.Min(r2 * 255 / a2, 255);
            g2 = (byte)Math.Min(g2 * 255 / a2, 255);
            b2 = (byte)Math.Min(b2 * 255 / a2, 255);
          }

          result[si] = b2;
          result[si + 1] = g2;
          result[si + 2] = r2;
          result[si + 3] = a2;
        }
      } else
        Buffer.BlockCopy(src, 0, result, 0, copyLen);

      return new() { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = result };
    }
  }

  /// <summary>Creates a <see cref="NieFile"/> from a <see cref="RawImage"/>.</summary>
  public static NieFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);
    var width = image.Width;
    var height = image.Height;
    var pixelCount = width * height;

    if (image.Format is PixelFormat.Rgba64 or PixelFormat.Rgb48 or PixelFormat.Gray16) {
      var rgba64 = image.Format == PixelFormat.Rgba64 ? image : PixelConverter.Convert(image, PixelFormat.Rgba64);
      var src = rgba64.PixelData;
      var result = new byte[pixelCount * 8];

      for (var i = 0; i < pixelCount; ++i) {
        var si = i * 8;
        var di = i * 8;
        // Source: R16-BE, G16-BE, B16-BE, A16-BE → Dest: B16-LE, G16-LE, R16-LE, A16-LE
        var r = (ushort)((src[si] << 8) | src[si + 1]);
        var g = (ushort)((src[si + 2] << 8) | src[si + 3]);
        var b = (ushort)((src[si + 4] << 8) | src[si + 5]);
        var a = (ushort)((src[si + 6] << 8) | src[si + 7]);

        result[di] = (byte)b;
        result[di + 1] = (byte)(b >> 8);
        result[di + 2] = (byte)g;
        result[di + 3] = (byte)(g >> 8);
        result[di + 4] = (byte)r;
        result[di + 5] = (byte)(r >> 8);
        result[di + 6] = (byte)a;
        result[di + 7] = (byte)(a >> 8);
      }

      return new() { Width = width, Height = height, PixelConfig = NiePixelConfig.Bgra16, PixelData = result };
    }

    {
      var bgra = PixelConverter.Convert(image, PixelFormat.Bgra32);
      var result = new byte[pixelCount * 4];
      Buffer.BlockCopy(bgra.PixelData, 0, result, 0, Math.Min(bgra.PixelData.Length, result.Length));
      return new() { Width = width, Height = height, PixelConfig = NiePixelConfig.Bgra8, PixelData = result };
    }
  }
}
