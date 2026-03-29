using System;

namespace FileFormat.SpookySpritesFalcon;

/// <summary>Assembles Spooky Sprites Atari Falcon compressed 16-bit true color file bytes from pixel data.</summary>
public static class SpookySpritesFalconWriter {

  public static byte[] ToBytes(SpookySpritesFalconFile file) => Assemble(file.PixelData, file.Width, file.Height);

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var compressed = SpookySpritesFalconRleCompressor.Compress(pixelData);
    var result = new byte[SpookySpritesFalconHeader.StructSize + compressed.Length];

    var header = new SpookySpritesFalconHeader((ushort)width, (ushort)height);
    header.WriteTo(result.AsSpan());

    compressed.AsSpan().CopyTo(result.AsSpan(SpookySpritesFalconHeader.StructSize));

    return result;
  }
}
