using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Iff;

namespace FileFormat.IffRgb8;

/// <summary>Assembles IFF RGB8 file bytes from an <see cref="IffRgb8File"/>.</summary>
public static class IffRgb8Writer {

  private const byte _NUM_PLANES = AmigaRgbRuns.Rgb8Bitplanes;

  public static byte[] ToBytes(IffRgb8File file) {
    ArgumentNullException.ThrowIfNull(file);

    var width = file.Width;
    var height = file.Height;
    var bodyData = AmigaRgbRuns.Pack(file.PixelData, width, height, deep: true);

    // Calculate sizes
    var bmhdChunkSize = 8 + Rgb8BmhdChunk.StructSize; // ID(4) + size(4) + data(20)
    var bodyChunkSize = 8 + bodyData.Length + (bodyData.Length & 1);
    var formDataSize = 4 + bmhdChunkSize + bodyChunkSize; // "RGB8" + chunks
    var totalSize = 8 + formDataSize; // "FORM" + size + data

    using var ms = new MemoryStream(totalSize);

    // FORM header
    _WriteChunkHeader(ms, "FORM", formDataSize);

    // Form type
    ms.Write("RGB8"u8);

    // BMHD chunk
    _WriteChunkHeader(ms, "BMHD", Rgb8BmhdChunk.StructSize);
    var bmhdBuffer = new byte[Rgb8BmhdChunk.StructSize];
    var bmhd = new Rgb8BmhdChunk(
      (ushort)width,
      (ushort)height,
      0,
      0,
      _NUM_PLANES,
      0,
      AmigaRgbRuns.CompressionMethod,
      0,
      0,
      1,
      1,
      (short)width,
      (short)height
    );
    bmhd.WriteTo(bmhdBuffer);
    ms.Write(bmhdBuffer);

    // BODY chunk
    _WriteChunkHeader(ms, "BODY", bodyData.Length);
    ms.Write(bodyData);
    if ((bodyData.Length & 1) != 0)
      ms.WriteByte(0); // pad to 2-byte alignment

    return ms.ToArray();
  }

  private static void _WriteChunkHeader(Stream stream, string chunkId, int size) {
    Span<byte> buffer = stackalloc byte[IffChunkHeader.StructSize];
    new Riff.FourCC(chunkId).WriteTo(buffer);
    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(buffer[4..], size);
    stream.Write(buffer);
  }
}
