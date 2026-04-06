using System;
using FileFormat.Core;
using FileFormat.Core.BlockDecoders;

namespace FileFormat.Astc;

/// <summary>In-memory representation of an ASTC (Adaptive Scalable Texture Compression) image.</summary>
[FormatMagicBytes([0x13, 0xAB, 0xA1, 0x5C])]
public readonly record struct AstcFile : IImageFormatReader<AstcFile>, IImageToRawImage<AstcFile>, IImageFromRawImage<AstcFile>, IImageFormatWriter<AstcFile> {

  static string IImageFormatMetadata<AstcFile>.PrimaryExtension => ".astc";
  static string[] IImageFormatMetadata<AstcFile>.FileExtensions => [".astc"];
  static AstcFile IImageFormatReader<AstcFile>.FromSpan(ReadOnlySpan<byte> data) => AstcReader.FromSpan(data);
  static byte[] IImageFormatWriter<AstcFile>.ToBytes(AstcFile file) => AstcWriter.ToBytes(file);
  public int Width { get; init; }
  public int Height { get; init; }
  public int Depth { get; init; }
  public byte BlockDimX { get; init; }
  public byte BlockDimY { get; init; }
  public byte BlockDimZ { get; init; }

  /// <summary>Raw ASTC compressed block data (16 bytes per block).</summary>
  public byte[] CompressedData { get; init; }

  /// <summary>Decodes the ASTC compressed data into a <see cref="RawImage"/> with RGBA32 pixels.</summary>
  public static RawImage ToRawImage(AstcFile file) {

    var width = file.Width;
    var height = file.Height;
    var output = new byte[width * height * 4];
    AstcBlockDecoder.DecodeImage(file.CompressedData, width, height, file.BlockDimX, file.BlockDimY, output);

    return new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgba32,
      PixelData = output,
    };
  }

  /// <summary>Creates an ASTC 4x4 image using void-extent blocks (each block is a solid color averaged from its pixels).</summary>
  public static AstcFile FromRawImage(RawImage image) {
    ArgumentNullException.ThrowIfNull(image);

    var rgba = PixelConverter.Convert(image, PixelFormat.Rgba32);
    var width = rgba.Width;
    var height = rgba.Height;
    var src = rgba.PixelData;

    const int blockW = 4;
    const int blockH = 4;
    const int blockBytes = 16;
    var blocksX = (width + blockW - 1) / blockW;
    var blocksY = (height + blockH - 1) / blockH;
    var compressed = new byte[blocksX * blocksY * blockBytes];

    for (var by = 0; by < blocksY; ++by)
      for (var bx = 0; bx < blocksX; ++bx) {
        // Average the pixels in this block
        int sumR = 0, sumG = 0, sumB = 0, sumA = 0, count = 0;
        for (var py = 0; py < blockH; ++py)
          for (var px = 0; px < blockW; ++px) {
            var x = bx * blockW + px;
            var y = by * blockH + py;
            if (x >= width || y >= height)
              continue;

            var si = (y * width + x) * 4;
            sumR += src[si];
            sumG += src[si + 1];
            sumB += src[si + 2];
            sumA += src[si + 3];
            ++count;
          }

        if (count == 0) count = 1;
        var avgR = (ushort)(sumR * 65535 / (count * 255));
        var avgG = (ushort)(sumG * 65535 / (count * 255));
        var avgB = (ushort)(sumB * 65535 / (count * 255));
        var avgA = (ushort)(sumA * 65535 / (count * 255));

        // Void-extent block: bits [8:0]=0x1FC (void-extent marker), [12:9]=1111 (all extents max)
        // Bytes 0-1: 0xFC 0xFD (void-extent marker in LE)
        // Bytes 2-7: reserved (zeros for 2D, min/max extents)
        // Bytes 8-15: RGBA as 16-bit LE values
        var di = (by * blocksX + bx) * blockBytes;
        compressed[di] = 0xFC;
        compressed[di + 1] = 0xFD;
        compressed[di + 2] = 0xFF;
        compressed[di + 3] = 0xFF;
        compressed[di + 4] = 0xFF;
        compressed[di + 5] = 0xFF;
        compressed[di + 6] = 0xFF;
        compressed[di + 7] = 0xFF;
        compressed[di + 8] = (byte)(avgR & 0xFF);
        compressed[di + 9] = (byte)(avgR >> 8);
        compressed[di + 10] = (byte)(avgG & 0xFF);
        compressed[di + 11] = (byte)(avgG >> 8);
        compressed[di + 12] = (byte)(avgB & 0xFF);
        compressed[di + 13] = (byte)(avgB >> 8);
        compressed[di + 14] = (byte)(avgA & 0xFF);
        compressed[di + 15] = (byte)(avgA >> 8);
      }

    return new() {
      Width = width,
      Height = height,
      Depth = 1,
      BlockDimX = blockW,
      BlockDimY = blockH,
      BlockDimZ = 1,
      CompressedData = compressed,
    };
  }
}
