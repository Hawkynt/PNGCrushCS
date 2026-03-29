using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Core.BlockDecoders;

namespace FileFormat.Pkm;

/// <summary>In-memory representation of a PKM (Ericsson Texture Container) file.</summary>
[FormatMagicBytes([0x50, 0x4B, 0x4D, 0x20])]
public sealed class PkmFile : IImageFileFormat<PkmFile> {

  static string IImageFileFormat<PkmFile>.PrimaryExtension => ".pkm";
  static string[] IImageFileFormat<PkmFile>.FileExtensions => [".pkm"];
  static PkmFile IImageFileFormat<PkmFile>.FromFile(FileInfo file) => PkmReader.FromFile(file);
  static PkmFile IImageFileFormat<PkmFile>.FromBytes(byte[] data) => PkmReader.FromBytes(data);
  static PkmFile IImageFileFormat<PkmFile>.FromStream(Stream stream) => PkmReader.FromStream(stream);
  static byte[] IImageFileFormat<PkmFile>.ToBytes(PkmFile file) => PkmWriter.ToBytes(file);
  /// <summary>Image width in pixels.</summary>
  public int Width { get; init; }
  /// <summary>Image height in pixels.</summary>
  public int Height { get; init; }
  /// <summary>Padded width (rounded up to 4-pixel blocks).</summary>
  public int PaddedWidth { get; init; }
  /// <summary>Padded height (rounded up to 4-pixel blocks).</summary>
  public int PaddedHeight { get; init; }
  /// <summary>The ETC compression format.</summary>
  public PkmFormat Format { get; init; }
  /// <summary>Version string: "10" for PKM 1.0 or "20" for PKM 2.0.</summary>
  public string Version { get; init; } = "10";
  /// <summary>The raw ETC-compressed block data.</summary>
  public byte[] CompressedData { get; init; } = [];

  /// <summary>Decodes the ETC-compressed data into a <see cref="RawImage"/> with RGBA32 pixels.</summary>
  public static RawImage ToRawImage(PkmFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var width = file.Width;
    var height = file.Height;
    var paddedWidth = file.PaddedWidth > 0 ? file.PaddedWidth : ((width + 3) / 4) * 4;
    var paddedHeight = file.PaddedHeight > 0 ? file.PaddedHeight : ((height + 3) / 4) * 4;
    var paddedOutput = new byte[paddedWidth * paddedHeight * 4];

    switch (file.Format) {
      case PkmFormat.Etc1Rgb:
        Etc1Decoder.DecodeImage(file.CompressedData, paddedWidth, paddedHeight, paddedOutput);
        break;
      case PkmFormat.Etc2Rgb:
        Etc2Decoder.DecodeEtc2RgbImage(file.CompressedData, paddedWidth, paddedHeight, paddedOutput);
        break;
      case PkmFormat.Etc2RgbA1:
        Etc2Decoder.DecodeEtc2RgbA1Image(file.CompressedData, paddedWidth, paddedHeight, paddedOutput);
        break;
      case PkmFormat.Etc2Rgba8:
        Etc2Decoder.DecodeEtc2RgbaImage(file.CompressedData, paddedWidth, paddedHeight, paddedOutput);
        break;
      case PkmFormat.Etc2R:
        Etc2Decoder.DecodeEacR11Image(file.CompressedData, paddedWidth, paddedHeight, paddedOutput);
        break;
      case PkmFormat.Etc2Rg:
        Etc2Decoder.DecodeEacRg11Image(file.CompressedData, paddedWidth, paddedHeight, paddedOutput);
        break;
      default:
        throw new NotSupportedException($"Unsupported PKM format: {file.Format}");
    }

    // Crop from padded dimensions to actual dimensions
    byte[] output;
    if (paddedWidth == width && paddedHeight == height)
      output = paddedOutput;
    else {
      output = new byte[width * height * 4];
      for (var y = 0; y < height; ++y)
        paddedOutput.AsSpan(y * paddedWidth * 4, width * 4).CopyTo(output.AsSpan(y * width * 4));
    }

    return new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgba32,
      PixelData = output,
    };
  }

  /// <summary>Creates a PKM 1.0 ETC1 image using individual mode (each 4x4 block encodes a single average color with no modulation).</summary>
  public static PkmFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgba = PixelConverter.Convert(image, PixelFormat.Rgba32);
    var width = rgba.Width;
    var height = rgba.Height;
    var src = rgba.PixelData;

    var paddedWidth = ((width + 3) / 4) * 4;
    var paddedHeight = ((height + 3) / 4) * 4;
    var blocksX = paddedWidth / 4;
    var blocksY = paddedHeight / 4;
    var compressed = new byte[blocksX * blocksY * 8];

    for (var by = 0; by < blocksY; ++by)
      for (var bx = 0; bx < blocksX; ++bx) {
        int sumR = 0, sumG = 0, sumB = 0, count = 0;
        for (var py = 0; py < 4; ++py)
          for (var px = 0; px < 4; ++px) {
            var x = bx * 4 + px;
            var y = by * 4 + py;
            if (x >= width || y >= height)
              continue;

            var si = (y * width + x) * 4;
            sumR += src[si];
            sumG += src[si + 1];
            sumB += src[si + 2];
            ++count;
          }

        if (count == 0) count = 1;
        // ETC1 individual mode: 4-bit color per sub-block
        var r4 = Math.Clamp(sumR / count >> 4, 0, 15);
        var g4 = Math.Clamp(sumG / count >> 4, 0, 15);
        var b4 = Math.Clamp(sumB / count >> 4, 0, 15);

        // ETC1 block format (8 bytes, big-endian):
        // Byte 0: R1[7:4] R2[3:0] (individual mode: both sub-blocks same color)
        // Byte 1: G1[7:4] G2[3:0]
        // Byte 2: B1[7:4] B2[3:0]
        // Byte 3: bit7=diff(0=individual), bit6-5=table1(0), bit4-3=table2(0), bit2=flipbit(0)
        // Bytes 4-7: all pixel indices = 0 (no modulation)
        var di = (by * blocksX + bx) * 8;
        compressed[di] = (byte)((r4 << 4) | r4);
        compressed[di + 1] = (byte)((g4 << 4) | g4);
        compressed[di + 2] = (byte)((b4 << 4) | b4);
        compressed[di + 3] = 0; // individual mode, table index 0, no flip
        // Bytes 4-7 = 0 (all pixel selectors = 00)
      }

    return new() {
      Width = width,
      Height = height,
      PaddedWidth = paddedWidth,
      PaddedHeight = paddedHeight,
      Format = PkmFormat.Etc1Rgb,
      Version = "10",
      CompressedData = compressed,
    };
  }
}
