using System;
using System.IO;

namespace FileFormat.Blp.Tests;

/// <summary>Helper methods for constructing minimal BLP2 files for testing.</summary>
internal static class BlpTestHelper {

  private const int _HEADER_SIZE = 148;
  private const int _PALETTE_SIZE = 1024;

  /// <summary>Builds a minimal palette-indexed BLP2 file.</summary>
  internal static byte[] BuildPaletteBlp(int width, int height, byte alphaDepth = 0) {
    var totalPixels = width * height;
    var alphaSize = alphaDepth switch {
      8 => totalPixels,
      4 => (totalPixels + 1) / 2,
      1 => (totalPixels + 7) / 8,
      _ => 0,
    };
    var mipDataSize = totalPixels + alphaSize;
    var dataOffset = (uint)(_HEADER_SIZE + _PALETTE_SIZE);
    var fileSize = (int)dataOffset + mipDataSize;
    var data = new byte[fileSize];

    using var ms = new MemoryStream(data);
    using var bw = new BinaryWriter(ms);

    // Magic "BLP2"
    bw.Write(0x32504C42U);
    // Type
    bw.Write(0U);
    // Encoding = Palette
    bw.Write((byte)1);
    // AlphaDepth
    bw.Write(alphaDepth);
    // AlphaEncoding
    bw.Write((byte)0);
    // HasMips
    bw.Write((byte)0);
    // Width, Height
    bw.Write((uint)width);
    bw.Write((uint)height);

    // MipOffsets[16]
    bw.Write(dataOffset); // mip 0
    for (var i = 1; i < 16; ++i)
      bw.Write(0U);

    // MipSizes[16]
    bw.Write((uint)mipDataSize); // mip 0
    for (var i = 1; i < 16; ++i)
      bw.Write(0U);

    // Palette (256 x BGRA)
    for (var i = 0; i < 256; ++i) {
      bw.Write((byte)(i * 3 % 256)); // B
      bw.Write((byte)(i * 5 % 256)); // G
      bw.Write((byte)(i * 7 % 256)); // R
      bw.Write((byte)255);            // A
    }

    // Pixel indices
    for (var i = 0; i < totalPixels; ++i)
      bw.Write((byte)(i % 256));

    // Alpha data
    for (var i = 0; i < alphaSize; ++i)
      bw.Write((byte)(i * 17 % 256));

    return data;
  }

  /// <summary>Builds a minimal uncompressed BGRA BLP2 file.</summary>
  internal static byte[] BuildBgraBlp(int width, int height) {
    var pixelDataSize = width * height * 4;
    var dataOffset = (uint)_HEADER_SIZE;
    var fileSize = _HEADER_SIZE + pixelDataSize;
    var data = new byte[fileSize];

    using var ms = new MemoryStream(data);
    using var bw = new BinaryWriter(ms);

    // Magic "BLP2"
    bw.Write(0x32504C42U);
    // Type
    bw.Write(0U);
    // Encoding = UncompressedBgra
    bw.Write((byte)3);
    // AlphaDepth
    bw.Write((byte)8);
    // AlphaEncoding
    bw.Write((byte)0);
    // HasMips
    bw.Write((byte)0);
    // Width, Height
    bw.Write((uint)width);
    bw.Write((uint)height);

    // MipOffsets[16]
    bw.Write(dataOffset);
    for (var i = 1; i < 16; ++i)
      bw.Write(0U);

    // MipSizes[16]
    bw.Write((uint)pixelDataSize);
    for (var i = 1; i < 16; ++i)
      bw.Write(0U);

    // Pixel data
    for (var i = 0; i < pixelDataSize; ++i)
      bw.Write((byte)(i % 256));

    return data;
  }
}
