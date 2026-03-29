using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.Wad2;

/// <summary>Assembles WAD2 file bytes from a <see cref="Wad2File"/>.</summary>
public static class Wad2Writer {

  private const int _MIPTEX_HEADER_SIZE = 40;

  public static byte[] ToBytes(Wad2File file) {
    ArgumentNullException.ThrowIfNull(file);

    var textures = file.Textures;
    var numLumps = textures.Count;

    var lumpSizes = new int[numLumps];
    var totalDataSize = 0;
    for (var i = 0; i < numLumps; ++i) {
      lumpSizes[i] = _CalculateLumpSize(textures[i]);
      totalDataSize += lumpSizes[i];
    }

    var directoryOffset = Wad2Header.StructSize + totalDataSize;
    var totalSize = directoryOffset + numLumps * Wad2Entry.StructSize;

    var result = new byte[totalSize];
    var span = result.AsSpan();

    var header = new Wad2Header((byte)'W', (byte)'A', (byte)'D', (byte)'2', numLumps, directoryOffset);
    header.WriteTo(span);

    var dataOffset = Wad2Header.StructSize;
    for (var i = 0; i < numLumps; ++i) {
      var texture = textures[i];
      var lumpSize = lumpSizes[i];

      _WriteMipTex(result, dataOffset, texture);

      var entry = new Wad2Entry(
        dataOffset,
        lumpSize,
        lumpSize,
        (byte)Wad2LumpType.MipTex,
        0,
        0,
        texture.Name
      );
      var entryOffset = directoryOffset + i * Wad2Entry.StructSize;
      entry.WriteTo(span[entryOffset..]);

      dataOffset += lumpSize;
    }

    return result;
  }

  private static int _CalculateLumpSize(Wad2Texture texture) {
    var w = texture.Width;
    var h = texture.Height;
    var mip0 = w * h;
    var mip1 = (w / 2) * (h / 2);
    var mip2 = (w / 4) * (h / 4);
    var mip3 = (w / 8) * (h / 8);

    // Header(40) + mip0 + mip1 + mip2 + mip3 (no palette for WAD2)
    return _MIPTEX_HEADER_SIZE + mip0 + mip1 + mip2 + mip3;
  }

  private static void _WriteMipTex(byte[] result, int offset, Wad2Texture texture) {
    var span = result.AsSpan();
    var w = texture.Width;
    var h = texture.Height;

    span.Slice(offset, 16).Clear();
    var nameBytes = Encoding.ASCII.GetBytes(texture.Name);
    var nameLen = Math.Min(nameBytes.Length, 15);
    nameBytes.AsSpan(0, nameLen).CopyTo(span.Slice(offset, 16));

    BinaryPrimitives.WriteUInt32LittleEndian(span[(offset + 16)..], (uint)w);
    BinaryPrimitives.WriteUInt32LittleEndian(span[(offset + 20)..], (uint)h);

    var mip0Size = w * h;
    var mip1Size = (w / 2) * (h / 2);
    var mip2Size = (w / 4) * (h / 4);
    var mip3Size = (w / 8) * (h / 8);

    var mipOffset0 = _MIPTEX_HEADER_SIZE;
    var mipOffset1 = mipOffset0 + mip0Size;
    var mipOffset2 = mipOffset1 + mip1Size;
    var mipOffset3 = mipOffset2 + mip2Size;

    BinaryPrimitives.WriteUInt32LittleEndian(span[(offset + 24)..], (uint)mipOffset0);
    BinaryPrimitives.WriteUInt32LittleEndian(span[(offset + 28)..], (uint)mipOffset1);
    BinaryPrimitives.WriteUInt32LittleEndian(span[(offset + 32)..], (uint)mipOffset2);
    BinaryPrimitives.WriteUInt32LittleEndian(span[(offset + 36)..], (uint)mipOffset3);

    texture.PixelData.AsSpan(0, Math.Min(texture.PixelData.Length, mip0Size)).CopyTo(result.AsSpan(offset + mipOffset0));

    if (texture.MipMaps is { Length: >= 3 }) {
      texture.MipMaps[0].AsSpan(0, Math.Min(texture.MipMaps[0].Length, mip1Size)).CopyTo(result.AsSpan(offset + mipOffset1));
      texture.MipMaps[1].AsSpan(0, Math.Min(texture.MipMaps[1].Length, mip2Size)).CopyTo(result.AsSpan(offset + mipOffset2));
      texture.MipMaps[2].AsSpan(0, Math.Min(texture.MipMaps[2].Length, mip3Size)).CopyTo(result.AsSpan(offset + mipOffset3));
    }
  }
}
