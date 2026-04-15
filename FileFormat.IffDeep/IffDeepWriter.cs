using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Iff;

namespace FileFormat.IffDeep;

/// <summary>Assembles IFF DEEP file bytes from an <see cref="IffDeepFile"/>.</summary>
public static class IffDeepWriter {

  public static byte[] ToBytes(IffDeepFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var width = file.Width;
    var height = file.Height;
    var hasAlpha = file.HasAlpha;
    var bytesPerPixel = hasAlpha ? 4 : 3;

    // Compress pixel data if requested
    byte[] bodyData;
    if (file.Compression == IffDeepCompression.Rle)
      bodyData = ByteRun1Compressor.Encode(file.PixelData);
    else
      bodyData = file.PixelData;

    // Build DPEL data: 3 color elements + optional alpha element
    var dpelElementCount = hasAlpha ? 4 : 3;
    var dpelData = new byte[dpelElementCount * 4];
    var dpelSpan = dpelData.AsSpan();
    // R element
    BinaryPrimitives.WriteUInt16BigEndian(dpelSpan, 0);     // type = color
    BinaryPrimitives.WriteUInt16BigEndian(dpelSpan[2..], 8); // 8 bits
    // G element
    BinaryPrimitives.WriteUInt16BigEndian(dpelSpan[4..], 0);
    BinaryPrimitives.WriteUInt16BigEndian(dpelSpan[6..], 8);
    // B element
    BinaryPrimitives.WriteUInt16BigEndian(dpelSpan[8..], 0);
    BinaryPrimitives.WriteUInt16BigEndian(dpelSpan[10..], 8);
    if (hasAlpha) {
      // A element
      BinaryPrimitives.WriteUInt16BigEndian(dpelSpan[12..], 1); // type = alpha
      BinaryPrimitives.WriteUInt16BigEndian(dpelSpan[14..], 8);
    }

    // Calculate chunk sizes
    var dgblChunkSize = 8 + 8; // ID(4) + size(4) + data(8)
    var dpelChunkSize = 8 + dpelData.Length + (dpelData.Length & 1);
    var bodyChunkSize = 8 + bodyData.Length + (bodyData.Length & 1);
    var formDataSize = 4 + dgblChunkSize + dpelChunkSize + bodyChunkSize; // "DEEP" + chunks
    var totalSize = 8 + formDataSize; // "FORM" + size + data

    using var ms = new MemoryStream(totalSize);

    // FORM header
    _WriteChunkHeader(ms, "FORM", formDataSize);

    // Form type
    ms.Write("DEEP"u8);

    // DGBL chunk
    _WriteChunkHeader(ms, "DGBL", 8);
    _WriteUInt16BigEndian(ms, (ushort)width);
    _WriteUInt16BigEndian(ms, (ushort)height);
    _WriteUInt16BigEndian(ms, (ushort)file.Compression);
    _WriteUInt16BigEndian(ms, (ushort)dpelElementCount);

    // DPEL chunk
    _WriteChunkHeader(ms, "DPEL", dpelData.Length);
    ms.Write(dpelData);
    if ((dpelData.Length & 1) != 0)
      ms.WriteByte(0);

    // BODY chunk
    _WriteChunkHeader(ms, "BODY", bodyData.Length);
    ms.Write(bodyData);
    if ((bodyData.Length & 1) != 0)
      ms.WriteByte(0);

    return ms.ToArray();
  }

  private static void _WriteChunkHeader(Stream stream, string chunkId, int size) {
    Span<byte> buffer = stackalloc byte[IffChunkHeader.StructSize];
    new Riff.FourCC(chunkId).WriteTo(buffer);
    BinaryPrimitives.WriteInt32BigEndian(buffer[4..], size);
    stream.Write(buffer);
  }

  private static void _WriteUInt16BigEndian(Stream stream, ushort value) {
    Span<byte> buffer = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
    stream.Write(buffer);
  }
}
