using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Blp;

/// <summary>In-memory representation of a BLP2 (Blizzard Texture) file.</summary>
[FormatMagicBytes([0x42, 0x4C, 0x50, 0x32])]
public sealed class BlpFile : IImageFileFormat<BlpFile> {

  static string IImageFileFormat<BlpFile>.PrimaryExtension => ".blp";
  static string[] IImageFileFormat<BlpFile>.FileExtensions => [".blp"];
  static BlpFile IImageFileFormat<BlpFile>.FromFile(FileInfo file) => BlpReader.FromFile(file);
  static BlpFile IImageFileFormat<BlpFile>.FromBytes(byte[] data) => BlpReader.FromBytes(data);
  static BlpFile IImageFileFormat<BlpFile>.FromStream(Stream stream) => BlpReader.FromStream(stream);
  static byte[] IImageFileFormat<BlpFile>.ToBytes(BlpFile file) => BlpWriter.ToBytes(file);

  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }

  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }

  /// <summary>Pixel encoding mode.</summary>
  public BlpEncoding Encoding { get; init; }

  /// <summary>Bits of alpha per pixel (0, 1, 4, or 8).</summary>
  public byte AlphaDepth { get; init; }

  /// <summary>DXT alpha encoding variant (only meaningful when <see cref="Encoding"/> is <see cref="BlpEncoding.Dxt"/>).</summary>
  public BlpAlphaEncoding AlphaEncoding { get; init; }

  /// <summary>Whether mipmaps are present.</summary>
  public bool HasMips { get; init; }

  /// <summary>The 256-entry BGRA palette (1024 bytes), or null if not palette-indexed.</summary>
  public byte[]? Palette { get; init; }

  /// <summary>Mipmap level data arrays. Index 0 is the full-resolution image.</summary>
  public byte[][] MipData { get; init; } = [];

  /// <summary>Converts a BLP file to a platform-independent BGRA32 raw image (mip level 0).</summary>
  public static RawImage ToRawImage(BlpFile file) {
    ArgumentNullException.ThrowIfNull(file);
    if (file.MipData.Length == 0 || file.MipData[0].Length == 0)
      throw new InvalidOperationException("BLP file contains no image data.");

    var width = file.Width;
    var height = file.Height;
    var mip0 = file.MipData[0];

    return file.Encoding switch {
      BlpEncoding.Palette => _DecodePalette(mip0, file.Palette ?? throw new InvalidOperationException("Palette encoding requires a palette."), width, height, file.AlphaDepth),
      BlpEncoding.Dxt => _DecodeDxt(mip0, width, height, file.AlphaEncoding),
      BlpEncoding.UncompressedBgra => _DecodeBgra(mip0, width, height),
      _ => throw new NotSupportedException($"Unsupported BLP encoding: {file.Encoding}.")
    };
  }

  /// <summary>Creates a BLP file from a platform-independent raw image using uncompressed BGRA encoding.</summary>
  public static BlpFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var bgra = image.ToBgra32();

    return new() {
      Width = image.Width,
      Height = image.Height,
      Encoding = BlpEncoding.UncompressedBgra,
      AlphaDepth = 8,
      AlphaEncoding = 0,
      HasMips = false,
      Palette = null,
      MipData = [bgra],
    };
  }

  private static RawImage _DecodePalette(byte[] data, byte[] palette, int width, int height, byte alphaDepth) {
    var totalPixels = width * height;
    var bgra = new byte[totalPixels * 4];

    // Indexed pixel data comes first, then alpha data
    for (var i = 0; i < totalPixels; ++i) {
      if (i >= data.Length)
        break;

      var index = data[i];
      var palOffset = index * 4; // Palette is BGRA
      if (palOffset + 3 >= palette.Length)
        continue;

      var dst = i * 4;
      bgra[dst] = palette[palOffset];         // B
      bgra[dst + 1] = palette[palOffset + 1]; // G
      bgra[dst + 2] = palette[palOffset + 2]; // R
      bgra[dst + 3] = 255;                    // default opaque
    }

    // Apply alpha from separate alpha data after the indexed pixels
    if (alphaDepth > 0) {
      var alphaStart = totalPixels;
      switch (alphaDepth) {
        case 8:
          for (var i = 0; i < totalPixels; ++i) {
            var srcIdx = alphaStart + i;
            if (srcIdx >= data.Length)
              break;
            bgra[i * 4 + 3] = data[srcIdx];
          }
          break;
        case 4:
          for (var i = 0; i < totalPixels; ++i) {
            var srcIdx = alphaStart + i / 2;
            if (srcIdx >= data.Length)
              break;
            var a4 = (i & 1) == 0 ? data[srcIdx] & 0x0F : (data[srcIdx] >> 4) & 0x0F;
            bgra[i * 4 + 3] = (byte)((a4 << 4) | a4);
          }
          break;
        case 1:
          for (var i = 0; i < totalPixels; ++i) {
            var srcIdx = alphaStart + i / 8;
            if (srcIdx >= data.Length)
              break;
            var bit = (data[srcIdx] >> (i % 8)) & 1;
            bgra[i * 4 + 3] = bit == 1 ? (byte)255 : (byte)0;
          }
          break;
      }
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = bgra };
  }

  private static RawImage _DecodeDxt(byte[] data, int width, int height, BlpAlphaEncoding alphaEncoding) {
    var pixels = alphaEncoding switch {
      BlpAlphaEncoding.Dxt1 => BlpDxtDecoder.DecodeDxt1Image(data, width, height),
      BlpAlphaEncoding.Dxt3 => BlpDxtDecoder.DecodeDxt3Image(data, width, height),
      BlpAlphaEncoding.Dxt5 => BlpDxtDecoder.DecodeDxt5Image(data, width, height),
      _ => BlpDxtDecoder.DecodeDxt1Image(data, width, height),
    };

    return new() { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = pixels };
  }

  private static RawImage _DecodeBgra(byte[] data, int width, int height) {
    var expected = width * height * 4;
    var pixels = new byte[expected];
    data.AsSpan(0, Math.Min(data.Length, expected)).CopyTo(pixels);
    return new() { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = pixels };
  }
}
