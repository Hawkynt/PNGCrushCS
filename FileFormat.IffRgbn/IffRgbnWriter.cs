using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FileFormat.IffRgbn;

/// <summary>Assembles IFF RGBN file bytes from an <see cref="IffRgbnFile"/>.</summary>
public static class IffRgbnWriter {

  private const byte _NUM_PLANES = 13;

  public static byte[] ToBytes(IffRgbnFile file) {
    ArgumentNullException.ThrowIfNull(file);

    var width = file.Width;
    var height = file.Height;
    var pixelCount = width * height;

    // Encode pixels: quantize to 4-bit channels, no RLE (repeat=0), genlock=0
    var bodyData = new byte[pixelCount * 2];
    for (var i = 0; i < pixelCount; ++i) {
      var srcOffset = i * 3;
      byte r = 0, g = 0, b = 0;
      if (srcOffset + 2 < file.PixelData.Length) {
        r = file.PixelData[srcOffset];
        g = file.PixelData[srcOffset + 1];
        b = file.PixelData[srcOffset + 2];
      }

      // Quantize 8-bit to 4-bit with rounding: (val + 8) / 17, clamped to 0..15
      var r4 = (byte)Math.Min((r + 8) / 17, 15);
      var g4 = (byte)Math.Min((g + 8) / 17, 15);
      var b4 = (byte)Math.Min((b + 8) / 17, 15);

      var dstOffset = i * 2;
      bodyData[dstOffset] = (byte)((r4 << 4) | g4);
      bodyData[dstOffset + 1] = (byte)(b4 << 4); // genlock=0, repeat=0
    }

    var bmhdChunkSize = 8 + RgbnBmhdChunk.StructSize;
    var bodyChunkSize = 8 + bodyData.Length + (bodyData.Length & 1);
    var formDataSize = 4 + bmhdChunkSize + bodyChunkSize;
    var totalSize = 8 + formDataSize;

    using var ms = new MemoryStream(totalSize);

    ms.Write(Encoding.ASCII.GetBytes("FORM"));
    _WriteInt32BigEndian(ms, formDataSize);

    ms.Write(Encoding.ASCII.GetBytes("RGBN"));

    ms.Write(Encoding.ASCII.GetBytes("BMHD"));
    _WriteInt32BigEndian(ms, RgbnBmhdChunk.StructSize);
    var bmhdBuffer = new byte[RgbnBmhdChunk.StructSize];
    var bmhd = new RgbnBmhdChunk(
      (ushort)width,
      (ushort)height,
      0,
      0,
      _NUM_PLANES,
      0,
      0,
      0,
      0,
      1,
      1,
      (short)width,
      (short)height
    );
    bmhd.WriteTo(bmhdBuffer);
    ms.Write(bmhdBuffer);

    ms.Write(Encoding.ASCII.GetBytes("BODY"));
    _WriteInt32BigEndian(ms, bodyData.Length);
    ms.Write(bodyData);
    if ((bodyData.Length & 1) != 0)
      ms.WriteByte(0);

    return ms.ToArray();
  }

  private static void _WriteInt32BigEndian(Stream stream, int value) {
    Span<byte> buffer = stackalloc byte[4];
    BinaryPrimitives.WriteInt32BigEndian(buffer, value);
    stream.Write(buffer);
  }
}
