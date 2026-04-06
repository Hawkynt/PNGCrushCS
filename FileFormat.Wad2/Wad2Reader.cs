using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Wad2;

/// <summary>Reads WAD2 files from bytes, streams, or file paths.</summary>
public static class Wad2Reader {

  private const int _MIPTEX_HEADER_SIZE = 40;

  public static Wad2File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("WAD2 file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Wad2File FromStream(Stream stream) {
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

  public static Wad2File FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static Wad2File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < Wad2Header.StructSize)
      throw new InvalidDataException("Data too small for a valid WAD2 file.");

    var span = data.AsSpan();
    var header = Wad2Header.ReadFrom(span);

    if (header.Magic1 != (byte)'W' || header.Magic2 != (byte)'A' || header.Magic3 != (byte)'D' || header.Magic4 != (byte)'2')
      throw new InvalidDataException("Invalid WAD2 signature.");

    var numLumps = header.NumLumps;
    var directoryOffset = header.DirectoryOffset;

    var requiredSize = directoryOffset + (long)numLumps * Wad2Entry.StructSize;
    if (data.Length < requiredSize)
      throw new InvalidDataException("Data too small for the declared WAD2 directory.");

    var textures = new List<Wad2Texture>();
    for (var i = 0; i < numLumps; ++i) {
      var entryOffset = directoryOffset + i * Wad2Entry.StructSize;
      var entry = Wad2Entry.ReadFrom(span[entryOffset..]);

      if (entry.Type != (byte)Wad2LumpType.MipTex)
        continue;

      var lumpStart = entry.FilePos;
      if (lumpStart + _MIPTEX_HEADER_SIZE > data.Length)
        continue;

      var texture = _ReadMipTex(data, lumpStart);
      textures.Add(texture);
    }

    return new Wad2File { Textures = textures };
  }

  private static Wad2Texture _ReadMipTex(byte[] data, int lumpStart) {
    var span = data.AsSpan();

    var nameBytes = span.Slice(lumpStart, 16);
    var nameEnd = nameBytes.IndexOf((byte)0);
    var name = nameEnd < 0
      ? Encoding.ASCII.GetString(nameBytes)
      : Encoding.ASCII.GetString(nameBytes[..nameEnd]);

    var width = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[(lumpStart + 16)..]);
    var height = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[(lumpStart + 20)..]);

    var mipOffset0 = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[(lumpStart + 24)..]);
    var mipOffset1 = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[(lumpStart + 28)..]);
    var mipOffset2 = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[(lumpStart + 32)..]);
    var mipOffset3 = (int)BinaryPrimitives.ReadUInt32LittleEndian(span[(lumpStart + 36)..]);

    var mip0Size = width * height;
    var pixelData = new byte[mip0Size];
    data.AsSpan(lumpStart + mipOffset0, mip0Size).CopyTo(pixelData.AsSpan(0));

    var mip1Size = (width / 2) * (height / 2);
    var mip2Size = (width / 4) * (height / 4);
    var mip3Size = (width / 8) * (height / 8);

    var mipMaps = new byte[3][];
    mipMaps[0] = new byte[mip1Size];
    mipMaps[1] = new byte[mip2Size];
    mipMaps[2] = new byte[mip3Size];

    data.AsSpan(lumpStart + mipOffset1, mip1Size).CopyTo(mipMaps[0].AsSpan(0));
    data.AsSpan(lumpStart + mipOffset2, mip2Size).CopyTo(mipMaps[1].AsSpan(0));
    data.AsSpan(lumpStart + mipOffset3, mip3Size).CopyTo(mipMaps[2].AsSpan(0));

    return new Wad2Texture {
      Name = name,
      Width = width,
      Height = height,
      PixelData = pixelData,
      MipMaps = mipMaps
    };
  }
}
