using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Iff;

namespace FileFormat.IffDeep;

/// <summary>Assembles IFF DEEP file bytes from an <see cref="IffDeepFile"/>.</summary>
public static class IffDeepWriter {

  /// <summary>The component types DPEL names, red through alpha.</summary>
  private const byte _Red = 1;

  private const byte _Green = 2;

  private const byte _Blue = 3;

  private const byte _Alpha = 4;

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

    // DPEL names each component in turn: how many there are, then a type and a bit count for each.
    // The count was missing here and every type was written as zero, which names no component at
    // all — so the chunk described a picture of nothing whatever the pixels held.
    var dpelElementCount = hasAlpha ? 4 : 3;
    var dpelData = new byte[4 + dpelElementCount * 4];
    var dpelSpan = dpelData.AsSpan();
    BinaryPrimitives.WriteUInt32BigEndian(dpelSpan, (uint)dpelElementCount);

    ReadOnlySpan<byte> types = [_Red, _Green, _Blue, _Alpha];
    for (var i = 0; i < dpelElementCount; ++i) {
      BinaryPrimitives.WriteUInt16BigEndian(dpelSpan[(4 + i * 4)..], types[i]);
      BinaryPrimitives.WriteUInt16BigEndian(dpelSpan[(6 + i * 4)..], 8);
    }

    var dgblChunkSize = 8 + 8;
    var dpelChunkSize = 8 + dpelData.Length + (dpelData.Length & 1);
    var bodyChunkSize = 8 + bodyData.Length + (bodyData.Length & 1);
    var formDataSize = 4 + dgblChunkSize + dpelChunkSize + bodyChunkSize;

    using var ms = new MemoryStream(8 + formDataSize);

    _WriteChunkHeader(ms, "FORM", formDataSize);
    ms.Write("DEEP"u8);

    // DGBL chunk
    _WriteChunkHeader(ms, "DGBL", 8);
    _WriteUInt16BigEndian(ms, (ushort)width);
    _WriteUInt16BigEndian(ms, (ushort)height);
    _WriteUInt16BigEndian(ms, (ushort)file.Compression);

    // The last word of DGBL is the pixel aspect, not the component count: square pixels are 1 to 1.
    ms.WriteByte(1);
    ms.WriteByte(1);

    // DPEL chunk
    _WriteChunkHeader(ms, "DPEL", dpelData.Length);
    ms.Write(dpelData);
    if ((dpelData.Length & 1) != 0)
      ms.WriteByte(0);

    // The pixels go in DBOD. A chunk named BODY is the ILBM name and a DEEP reader passes over it,
    // leaving a file with a header and no picture.
    _WriteChunkHeader(ms, "DBOD", bodyData.Length);
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
