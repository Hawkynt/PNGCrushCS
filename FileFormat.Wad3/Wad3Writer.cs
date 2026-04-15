using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Wad3;

/// <summary>Assembles WAD3 file bytes from a <see cref="Wad3File"/>.</summary>
public static class Wad3Writer {

  public static byte[] ToBytes(Wad3File file) {
    ArgumentNullException.ThrowIfNull(file);

    var textures = file.Textures;
    var numLumps = textures.Count;

    // Calculate lump sizes and total data
    var lumpSizes = new int[numLumps];
    var totalDataSize = 0;
    for (var i = 0; i < numLumps; ++i) {
      lumpSizes[i] = _CalculateLumpSize(textures[i]);
      totalDataSize += lumpSizes[i];
    }

    var directoryOffset = Wad3Header.StructSize + totalDataSize;
    var totalSize = directoryOffset + numLumps * Wad3Entry.StructSize;

    var result = new byte[totalSize];
    var span = result.AsSpan();

    // Write header
    var header = new Wad3Header((byte)'W', (byte)'A', (byte)'D', (byte)'3', numLumps, directoryOffset);
    header.WriteTo(span);

    // Write each texture lump and directory entry
    var dataOffset = Wad3Header.StructSize;
    for (var i = 0; i < numLumps; ++i) {
      var texture = textures[i];
      var lumpSize = lumpSizes[i];

      _WriteMipTex(result, dataOffset, texture);

      var entry = new Wad3Entry(
        dataOffset,
        lumpSize,
        lumpSize,
        (byte)Wad3LumpType.MipTex,
        0,
        0,
        texture.Name
      );
      var entryOffset = directoryOffset + i * Wad3Entry.StructSize;
      entry.WriteTo(span[entryOffset..]);

      dataOffset += lumpSize;
    }

    return result;
  }

  private static int _CalculateLumpSize(Wad3Texture texture) {
    var w = texture.Width;
    var h = texture.Height;
    var mip0 = w * h;
    var mip1 = (w / 2) * (h / 2);
    var mip2 = (w / 4) * (h / 4);
    var mip3 = (w / 8) * (h / 8);

    // Header(40) + mip0 + mip1 + mip2 + mip3 + palette count(2) + palette(768)
    return Wad3MipTexHeader.StructSize + mip0 + mip1 + mip2 + mip3 + 2 + 768;
  }

  private static void _WriteMipTex(byte[] result, int offset, Wad3Texture texture) {
    var w = texture.Width;
    var h = texture.Height;

    var mip0Size = w * h;
    var mip1Size = (w / 2) * (h / 2);
    var mip2Size = (w / 4) * (h / 4);
    var mip3Size = (w / 8) * (h / 8);

    var mipOffset0 = (uint)Wad3MipTexHeader.StructSize;
    var mipOffset1 = mipOffset0 + (uint)mip0Size;
    var mipOffset2 = mipOffset1 + (uint)mip1Size;
    var mipOffset3 = mipOffset2 + (uint)mip2Size;

    var mipTexHeader = new Wad3MipTexHeader(
      texture.Name, (uint)w, (uint)h,
      mipOffset0, mipOffset1, mipOffset2, mipOffset3
    );
    mipTexHeader.WriteTo(result.AsSpan(offset));

    // Write mip level 0
    texture.PixelData.AsSpan(0, Math.Min(texture.PixelData.Length, mip0Size)).CopyTo(result.AsSpan(offset + (int)mipOffset0));

    // Write mip levels 1-3
    if (texture.MipMaps is { Length: >= 3 }) {
      texture.MipMaps[0].AsSpan(0, Math.Min(texture.MipMaps[0].Length, mip1Size)).CopyTo(result.AsSpan(offset + (int)mipOffset1));
      texture.MipMaps[1].AsSpan(0, Math.Min(texture.MipMaps[1].Length, mip2Size)).CopyTo(result.AsSpan(offset + (int)mipOffset2));
      texture.MipMaps[2].AsSpan(0, Math.Min(texture.MipMaps[2].Length, mip3Size)).CopyTo(result.AsSpan(offset + (int)mipOffset3));
    }

    // Palette count (256) + palette data
    var paletteOffset = offset + (int)mipOffset3 + mip3Size;
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(paletteOffset), 256);
    var paletteLen = Math.Min(texture.Palette.Length, 768);
    texture.Palette.AsSpan(0, paletteLen).CopyTo(result.AsSpan(paletteOffset + 2));
  }
}
