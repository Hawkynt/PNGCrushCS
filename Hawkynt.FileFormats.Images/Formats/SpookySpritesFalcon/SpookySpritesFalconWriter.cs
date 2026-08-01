using System;
using System.Text;

namespace FileFormat.SpookySpritesFalcon;

/// <summary>Assembles Spooky Sprites Atari Falcon file bytes.</summary>
public static class SpookySpritesFalconWriter {

  public static byte[] ToBytes(SpookySpritesFalconFile file) => Assemble(file.PixelData, file.Width, file.Height);

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var compressed = SpookySpritesFalconRleCompressor.Compress(pixelData ?? [], width * height);
    var result = new byte[SpookySpritesFalconHeader.StructSize + compressed.Length];

    // The size first, then the name over the filler the serializer puts at the front — the other
    // way round and the filler erases it.
    new SpookySpritesFalconHeader((ushort)width, (ushort)height).WriteTo(result.AsSpan());
    Encoding.ASCII.GetBytes(SpookySpritesFalconHeader.Signature).CopyTo(result, 0);
    compressed.CopyTo(result, SpookySpritesFalconHeader.StructSize);

    return result;
  }
}
