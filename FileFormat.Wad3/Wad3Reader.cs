using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Wad3;

/// <summary>Reads WAD3 files from bytes, streams, or file paths.</summary>
public static class Wad3Reader {

  private const int _MIPTEX_HEADER_SIZE = 40;

  public static Wad3File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("WAD3 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Wad3File FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }
    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static Wad3File FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < Wad3Header.StructSize)
      throw new InvalidDataException("Data too small for a valid WAD3 file.");

    var header = Wad3Header.ReadFrom(data);

    if (header.Magic1 != (byte)'W' || header.Magic2 != (byte)'A' || header.Magic3 != (byte)'D' || header.Magic4 != (byte)'3')
      throw new InvalidDataException("Invalid WAD3 signature.");

    var numLumps = header.NumLumps;
    var directoryOffset = header.DirectoryOffset;

    var requiredSize = directoryOffset + (long)numLumps * Wad3Entry.StructSize;
    if (data.Length < requiredSize)
      throw new InvalidDataException("Data too small for the declared WAD3 directory.");

    var textures = new List<Wad3Texture>();
    for (var i = 0; i < numLumps; ++i) {
      var entryOffset = directoryOffset + i * Wad3Entry.StructSize;
      var entry = Wad3Entry.ReadFrom(data[entryOffset..]);

      if (entry.Type != (byte)Wad3LumpType.MipTex)
        continue;

      var lumpStart = entry.FilePos;
      if (lumpStart + _MIPTEX_HEADER_SIZE > data.Length)
        continue;

      var texture = _ReadMipTex(data, lumpStart);
      textures.Add(texture);
    }

    return new Wad3File { Textures = textures };
  }

  public static Wad3File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  private static Wad3Texture _ReadMipTex(ReadOnlySpan<byte> data, int lumpStart) {

    // Read MipTex header (40 bytes)
    var nameBytes = data.Slice(lumpStart, 16);
    var nameEnd = nameBytes.IndexOf((byte)0);
    var name = nameEnd < 0
      ? Encoding.ASCII.GetString(nameBytes)
      : Encoding.ASCII.GetString(nameBytes[..nameEnd]);

    var width = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[(lumpStart + 16)..]);
    var height = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[(lumpStart + 20)..]);

    var mipOffset0 = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[(lumpStart + 24)..]);
    var mipOffset1 = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[(lumpStart + 28)..]);
    var mipOffset2 = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[(lumpStart + 32)..]);
    var mipOffset3 = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[(lumpStart + 36)..]);

    // Mip level 0 pixel data
    var mip0Size = width * height;
    var pixelData = new byte[mip0Size];
    data.Slice(lumpStart + mipOffset0, mip0Size).CopyTo(pixelData.AsSpan(0));

    // Mip levels 1-3
    var mip1Size = (width / 2) * (height / 2);
    var mip2Size = (width / 4) * (height / 4);
    var mip3Size = (width / 8) * (height / 8);

    var mipMaps = new byte[3][];
    mipMaps[0] = new byte[mip1Size];
    mipMaps[1] = new byte[mip2Size];
    mipMaps[2] = new byte[mip3Size];

    data.Slice(lumpStart + mipOffset1, mip1Size).CopyTo(mipMaps[0].AsSpan(0));
    data.Slice(lumpStart + mipOffset2, mip2Size).CopyTo(mipMaps[1].AsSpan(0));
    data.Slice(lumpStart + mipOffset3, mip3Size).CopyTo(mipMaps[2].AsSpan(0));

    // Palette: after mip3 data, 2-byte count then 768 bytes RGB
    var paletteCountOffset = lumpStart + mipOffset3 + mip3Size;
    var paletteCount = BinaryPrimitives.ReadUInt16LittleEndian(data[paletteCountOffset..]);
    var paletteSize = paletteCount * 3;
    var palette = new byte[paletteSize];
    data.Slice(paletteCountOffset + 2, paletteSize).CopyTo(palette.AsSpan(0));

    return new Wad3Texture {
      Name = name,
      Width = width,
      Height = height,
      PixelData = pixelData,
      MipMaps = mipMaps,
      Palette = palette
    };
  }
}
