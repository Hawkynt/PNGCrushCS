using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FileFormat.Ktx;

/// <summary>Reads KTX files from bytes, streams, or file paths.</summary>
public static class KtxReader {

  public static KtxFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("KTX file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static KtxFile FromStream(Stream stream) {
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

  public static KtxFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static KtxFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < KtxHeader.IdentifierSize)
      throw new InvalidDataException("Data too small for a valid KTX file.");

    var span = data.AsSpan();
    if (_MatchesIdentifier(span, KtxHeader.Identifier))
      return _ParseKtx1(data);

    if (_MatchesIdentifier(span, Ktx2Header.Identifier))
      return _ParseKtx2(data);

    throw new InvalidDataException("Invalid KTX identifier.");
  }

  private static bool _MatchesIdentifier(ReadOnlySpan<byte> data, byte[] identifier) {
    if (data.Length < identifier.Length)
      return false;

    for (var i = 0; i < identifier.Length; ++i)
      if (data[i] != identifier[i])
        return false;

    return true;
  }

  private static KtxFile _ParseKtx1(byte[] data) {
    if (data.Length < KtxHeader.StructSize)
      throw new InvalidDataException("Data too small for a valid KTX1 file.");

    var header = KtxHeader.ReadFrom(data.AsSpan());
    var isBigEndian = header.Endianness == KtxHeader.EndiannessBE;

    var offset = KtxHeader.StructSize;

    // Read key-value data
    var keyValues = _ReadKeyValues(data, ref offset, header.BytesOfKeyValueData, isBigEndian);

    // Read mip levels
    var mipCount = Math.Max(1, header.NumberOfMipmapLevels);
    var mipLevels = new List<KtxMipLevel>(mipCount);
    for (var level = 0; level < mipCount; ++level) {
      if (offset + 4 > data.Length)
        break;

      var imageSize = isBigEndian
        ? BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset))
        : BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
      offset += 4;

      var mipData = new byte[imageSize];
      if (offset + imageSize <= data.Length)
        data.AsSpan(offset, imageSize).CopyTo(mipData.AsSpan(0));

      var mipWidth = Math.Max(1, header.PixelWidth >> level);
      var mipHeight = Math.Max(1, header.PixelHeight >> level);

      mipLevels.Add(new KtxMipLevel { Width = mipWidth, Height = mipHeight, Data = mipData });

      offset += imageSize;
      // Align to 4 bytes
      offset = (offset + 3) & ~3;
    }

    return new KtxFile {
      Width = header.PixelWidth,
      Height = header.PixelHeight,
      Depth = header.PixelDepth,
      Version = KtxVersion.Ktx1,
      MipmapCount = mipCount,
      Faces = header.NumberOfFaces,
      ArrayElements = header.NumberOfArrayElements,
      MipLevels = mipLevels,
      KeyValues = keyValues.Count > 0 ? keyValues : null,
      GlType = header.GlType,
      GlTypeSize = header.GlTypeSize,
      GlFormat = header.GlFormat,
      GlInternalFormat = header.GlInternalFormat,
      GlBaseInternalFormat = header.GlBaseInternalFormat
    };
  }

  private static KtxFile _ParseKtx2(byte[] data) {
    if (data.Length < Ktx2Header.StructSize)
      throw new InvalidDataException("Data too small for a valid KTX2 file.");

    var header = Ktx2Header.ReadFrom(data.AsSpan());

    // Read key-value data
    var keyValues = new List<KtxKeyValue>();
    if (header.KvdByteLength > 0 && header.KvdByteOffset + header.KvdByteLength <= data.Length)
      keyValues = _ReadKeyValues(data, header.KvdByteOffset, header.KvdByteLength);

    // Read mip levels via level index
    var levelCount = Math.Max(1, header.LevelCount);
    var mipLevels = new List<KtxMipLevel>(levelCount);
    var levelIndexOffset = Ktx2Header.StructSize;

    for (var level = 0; level < levelCount; ++level) {
      var indexEntry = levelIndexOffset + level * 24; // 3 x int64 per level
      if (indexEntry + 24 > data.Length)
        break;

      var byteOffset = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(indexEntry));
      var byteLength = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(indexEntry + 8));

      var mipData = new byte[byteLength];
      if (byteOffset + byteLength <= data.Length)
        data.AsSpan((int)byteOffset, (int)byteLength).CopyTo(mipData.AsSpan(0));

      var mipWidth = Math.Max(1, header.PixelWidth >> level);
      var mipHeight = Math.Max(1, header.PixelHeight >> level);

      mipLevels.Add(new KtxMipLevel { Width = mipWidth, Height = mipHeight, Data = mipData });
    }

    return new KtxFile {
      Width = header.PixelWidth,
      Height = header.PixelHeight,
      Depth = header.PixelDepth,
      Version = KtxVersion.Ktx2,
      MipmapCount = levelCount,
      Faces = header.FaceCount,
      ArrayElements = header.LayerCount,
      MipLevels = mipLevels,
      KeyValues = keyValues.Count > 0 ? keyValues : null,
      VkFormat = header.VkFormat,
      TypeSize = header.TypeSize,
      SupercompressionScheme = header.SupercompressionScheme
    };
  }

  private static List<KtxKeyValue> _ReadKeyValues(byte[] data, ref int offset, int bytesOfKeyValueData, bool isBigEndian) {
    var result = new List<KtxKeyValue>();
    var end = offset + bytesOfKeyValueData;

    while (offset + 4 <= end && offset + 4 <= data.Length) {
      var keyAndValueByteSize = isBigEndian
        ? BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset))
        : BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
      offset += 4;

      if (keyAndValueByteSize <= 0 || offset + keyAndValueByteSize > data.Length)
        break;

      var kvData = data.AsSpan(offset, keyAndValueByteSize);
      var nullIndex = kvData.IndexOf((byte)0);
      if (nullIndex >= 0) {
        var key = Encoding.UTF8.GetString(kvData[..nullIndex]);
        var valueStart = nullIndex + 1;
        var value = valueStart < keyAndValueByteSize ? kvData[valueStart..keyAndValueByteSize].ToArray() : [];
        result.Add(new KtxKeyValue { Key = key, Value = value });
      }

      offset += keyAndValueByteSize;
      // Align to 4 bytes
      offset = (offset + 3) & ~3;
    }

    return result;
  }

  private static List<KtxKeyValue> _ReadKeyValues(byte[] data, int kvdOffset, int kvdLength) {
    var result = new List<KtxKeyValue>();
    var offset = kvdOffset;
    var end = kvdOffset + kvdLength;

    while (offset + 4 <= end && offset + 4 <= data.Length) {
      var keyAndValueByteSize = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
      offset += 4;

      if (keyAndValueByteSize <= 0 || offset + keyAndValueByteSize > data.Length)
        break;

      var kvData = data.AsSpan(offset, keyAndValueByteSize);
      var nullIndex = kvData.IndexOf((byte)0);
      if (nullIndex >= 0) {
        var key = Encoding.UTF8.GetString(kvData[..nullIndex]);
        var valueStart = nullIndex + 1;
        var value = valueStart < keyAndValueByteSize ? kvData[valueStart..keyAndValueByteSize].ToArray() : [];
        result.Add(new KtxKeyValue { Key = key, Value = value });
      }

      offset += keyAndValueByteSize;
      // Align to 4 bytes
      offset = (offset + 3) & ~3;
    }

    return result;
  }
}
