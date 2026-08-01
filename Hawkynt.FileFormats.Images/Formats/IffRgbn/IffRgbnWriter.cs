using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Iff;

namespace FileFormat.IffRgbn;

/// <summary>Assembles IFF RGBN file bytes from an <see cref="IffRgbnFile"/>.</summary>
public static class IffRgbnWriter {

  private const byte _NUM_PLANES = AmigaRgbRuns.RgbnBitplanes;

  public static byte[] ToBytes(IffRgbnFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var width = file.Width;
    var height = file.Height;

    // The run count lives in each unit's low bits, and a zero there does not mean "one pixel" — it
    // means the count follows in another byte. Writing zero everywhere, which is what this did,
    // makes every unit swallow the next one as its length.
    var bodyData = AmigaRgbRuns.Pack(file.PixelData, width, height, deep: false);

    var bmhdChunkSize = 8 + RgbnBmhdChunk.StructSize;
    var bodyChunkSize = 8 + bodyData.Length + (bodyData.Length & 1);
    var formDataSize = 4 + bmhdChunkSize + bodyChunkSize;
    var totalSize = 8 + formDataSize;

    using var ms = new MemoryStream(totalSize);

    _WriteChunkHeader(ms, "FORM", formDataSize);

    ms.Write("RGBN"u8);

    _WriteChunkHeader(ms, "BMHD", RgbnBmhdChunk.StructSize);
    var bmhdBuffer = new byte[RgbnBmhdChunk.StructSize];
    var bmhd = new RgbnBmhdChunk(
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

    _WriteChunkHeader(ms, "BODY", bodyData.Length);
    ms.Write(bodyData);
    if ((bodyData.Length & 1) != 0)
      ms.WriteByte(0);

    return ms.ToArray();
  }

  private static void _WriteChunkHeader(Stream stream, string chunkId, int size) {
    Span<byte> buffer = stackalloc byte[IffChunkHeader.StructSize];
    new Riff.FourCC(chunkId).WriteTo(buffer);
    System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(buffer[4..], size);
    stream.Write(buffer);
  }
}
