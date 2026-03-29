using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.IffDeep;

/// <summary>Reads IFF DEEP files from bytes, streams, or file paths.</summary>
public static class IffDeepReader {

  private const int _MIN_IFF_SIZE = 12; // "FORM" + size + form type

  public static IffDeepFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("IFF DEEP file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IffDeepFile FromStream(Stream stream) {
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

  public static IffDeepFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < _MIN_IFF_SIZE)
      throw new InvalidDataException("Data too small for a valid IFF DEEP file.");

    var span = data.AsSpan();

    // Validate FORM magic
    var formId = Encoding.ASCII.GetString(data, 0, 4);
    if (formId != "FORM")
      throw new InvalidDataException($"Invalid IFF magic: expected 'FORM', got '{formId}'.");

    // Validate DEEP form type
    var formType = Encoding.ASCII.GetString(data, 8, 4);
    if (formType != "DEEP")
      throw new InvalidDataException($"Invalid IFF form type: expected 'DEEP', got '{formType}'.");

    var formSize = BinaryPrimitives.ReadInt32BigEndian(span[4..]);

    // Parse chunks
    int width = 0, height = 0;
    var compression = IffDeepCompression.None;
    var hasAlpha = false;
    byte[]? bodyData = null;
    var hasDgbl = false;

    var offset = 12;
    var endOffset = Math.Min(8 + formSize, data.Length);

    while (offset + 8 <= endOffset) {
      var chunkId = Encoding.ASCII.GetString(data, offset, 4);
      var chunkSize = BinaryPrimitives.ReadInt32BigEndian(span[(offset + 4)..]);
      var chunkDataOffset = offset + 8;

      if (chunkDataOffset + chunkSize > data.Length)
        break;

      switch (chunkId) {
        case "DGBL":
          if (chunkSize >= 8) {
            width = BinaryPrimitives.ReadUInt16BigEndian(span[chunkDataOffset..]);
            height = BinaryPrimitives.ReadUInt16BigEndian(span[(chunkDataOffset + 2)..]);
            compression = (IffDeepCompression)BinaryPrimitives.ReadUInt16BigEndian(span[(chunkDataOffset + 4)..]);
            hasDgbl = true;
          }
          break;
        case "DPEL":
          hasAlpha = _ParseDpelChunk(span.Slice(chunkDataOffset, chunkSize));
          break;
        case "DBOD":
        case "BODY":
          bodyData = new byte[chunkSize];
          span.Slice(chunkDataOffset, chunkSize).CopyTo(bodyData);
          break;
      }

      // Advance to next chunk (2-byte aligned)
      offset = chunkDataOffset + chunkSize + (chunkSize & 1);
    }

    if (!hasDgbl)
      throw new InvalidDataException("IFF DEEP file missing required DGBL chunk.");

    // Decompress pixel data if needed
    var bytesPerPixel = hasAlpha ? 4 : 3;
    var expectedPixelBytes = width * height * bytesPerPixel;

    byte[] pixelData;
    if (bodyData == null)
      pixelData = new byte[expectedPixelBytes];
    else if (compression == IffDeepCompression.Rle)
      pixelData = ByteRun1Compressor.Decode(bodyData, expectedPixelBytes);
    else
      pixelData = bodyData.Length >= expectedPixelBytes ? bodyData[..expectedPixelBytes] : bodyData;

    return new IffDeepFile {
      Width = width,
      Height = height,
      HasAlpha = hasAlpha,
      Compression = compression,
      PixelData = pixelData,
    };
  }

  /// <summary>Parses a DPEL chunk to determine whether alpha is present.</summary>
  private static bool _ParseDpelChunk(ReadOnlySpan<byte> data) {
    // Each element descriptor is 4 bytes: uint16 type, uint16 bits
    var elementCount = data.Length / 4;
    for (var i = 0; i < elementCount; ++i) {
      var type = BinaryPrimitives.ReadUInt16BigEndian(data[(i * 4)..]);
      if (type == 1) // 1 = alpha
        return true;
    }
    return false;
  }
}
