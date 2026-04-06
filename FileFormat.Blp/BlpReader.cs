using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Blp;

/// <summary>Reads BLP2 (Blizzard Texture) files from bytes, streams, or file paths.</summary>
public static class BlpReader {

  private const int _MAGIC = 0x32504C42; // "BLP2" as uint32 LE
  private const int _HEADER_SIZE = 148;
  private const int _PALETTE_SIZE = 1024; // 256 entries x 4 bytes (BGRA)
  private const int _MAX_MIPS = 16;

  public static BlpFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("BLP file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static BlpFile FromStream(Stream stream) {
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

  public static BlpFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static BlpFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _HEADER_SIZE)
      throw new InvalidDataException("Data too small for a valid BLP2 file.");

    var span = data.AsSpan();

    var magic = BinaryPrimitives.ReadUInt32LittleEndian(span);
    if (magic != _MAGIC)
      throw new InvalidDataException("Invalid BLP2 magic number.");

    var type = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4));
    var encoding = (BlpEncoding)span[8];
    var alphaDepth = span[9];
    var alphaEncoding = (BlpAlphaEncoding)span[10];
    var hasMips = span[11] != 0;
    var width = (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(12));
    var height = (int)BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(16));

    // Read mip offsets and sizes
    var mipOffsets = new uint[_MAX_MIPS];
    var mipSizes = new uint[_MAX_MIPS];
    for (var i = 0; i < _MAX_MIPS; ++i) {
      mipOffsets[i] = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(20 + i * 4));
      mipSizes[i] = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(84 + i * 4));
    }

    // Count actual mipmap levels
    var mipCount = 0;
    for (var i = 0; i < _MAX_MIPS; ++i) {
      if (mipSizes[i] == 0 || mipOffsets[i] == 0)
        break;
      ++mipCount;
    }

    // Read palette if palette-indexed
    byte[]? palette = null;
    if (encoding == BlpEncoding.Palette) {
      if (data.Length < _HEADER_SIZE + _PALETTE_SIZE)
        throw new InvalidDataException("Data too small to contain BLP palette.");

      palette = new byte[_PALETTE_SIZE];
      data.AsSpan(_HEADER_SIZE, _PALETTE_SIZE).CopyTo(palette.AsSpan(0));
    }

    // Read mipmap data
    var mipData = new byte[mipCount][];
    for (var i = 0; i < mipCount; ++i) {
      var offset = (int)mipOffsets[i];
      var size = (int)mipSizes[i];
      mipData[i] = new byte[size];

      var available = Math.Min(size, data.Length - offset);
      if (available > 0 && offset >= 0 && offset < data.Length)
        data.AsSpan(offset, available).CopyTo(mipData[i].AsSpan(0));
    }

    return new() {
      Width = width,
      Height = height,
      Encoding = encoding,
      AlphaDepth = alphaDepth,
      AlphaEncoding = alphaEncoding,
      HasMips = hasMips,
      Palette = palette,
      MipData = mipData,
    };
  }
}
