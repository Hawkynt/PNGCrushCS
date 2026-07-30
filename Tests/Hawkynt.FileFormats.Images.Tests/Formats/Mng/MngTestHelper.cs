using System;
using System.IO;
using System.IO.Hashing;

namespace FileFormat.Mng.Tests;

/// <summary>Helper methods for building minimal MNG and PNG test data.</summary>
internal static class MngTestHelper {

  private static readonly byte[] _MNG_SIGNATURE = { 0x8A, 0x4D, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
  private static readonly byte[] _PNG_SIGNATURE = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

  /// <summary>Builds a minimal valid PNG file (1x1 RGB, single black pixel).</summary>
  internal static byte[] BuildMinimalPng() {
    using var ms = new MemoryStream();

    // PNG signature
    ms.Write(_PNG_SIGNATURE);

    // IHDR chunk: 13 bytes of data
    var ihdrData = new byte[13];
    _WriteUInt32BE(ihdrData.AsSpan(0), 1);  // width
    _WriteUInt32BE(ihdrData.AsSpan(4), 1);  // height
    ihdrData[8] = 8;  // bit depth
    ihdrData[9] = 2;  // color type = RGB
    ihdrData[10] = 0; // compression method
    ihdrData[11] = 0; // filter method
    ihdrData[12] = 0; // interlace method
    _WriteChunk(ms, "IHDR", ihdrData);

    // IDAT chunk: zlib-wrapped deflate of filter byte (0) + 3 RGB bytes (0,0,0)
    // Minimal zlib: 78 01 (CMF, FLG) + deflate block + Adler32
    // Deflate final block: 01 04 00 FB FF 00 00 00 00 (literal block: filter=0, R=0, G=0, B=0)
    // Adler32 of {0,0,0,0} = 00 01 00 01
    var idatData = new byte[] {
      0x78, 0x01,                         // zlib header
      0x01, 0x04, 0x00, 0xFB, 0xFF,       // deflate final block, len=4, nlen=~4
      0x00, 0x00, 0x00, 0x00,             // filter=0, R=0, G=0, B=0
      0x00, 0x01, 0x00, 0x01              // Adler32
    };
    _WriteChunk(ms, "IDAT", idatData);

    // IEND chunk: 0 bytes of data
    _WriteChunk(ms, "IEND", []);

    return ms.ToArray();
  }

  /// <summary>Builds a minimal MNG file with the given frames.</summary>
  internal static byte[] BuildMinimalMng(int width, int height, int ticksPerSecond, params byte[][] frames) {
    var file = new MngFile {
      Width = width,
      Height = height,
      TicksPerSecond = ticksPerSecond,
      Frames = frames
    };
    return MngWriter.ToBytes(file);
  }

  private static void _WriteChunk(Stream stream, string type, byte[] data) {
    var lengthBytes = new byte[4];
    _WriteUInt32BE(lengthBytes, (uint)data.Length);
    stream.Write(lengthBytes);

    var typeBytes = new byte[4];
    for (var i = 0; i < 4; ++i)
      typeBytes[i] = (byte)type[i];
    stream.Write(typeBytes);

    if (data.Length > 0)
      stream.Write(data);

    var crc = new Crc32();
    crc.Append(typeBytes);
    if (data.Length > 0)
      crc.Append(data);

    var crcValue = crc.GetCurrentHashAsUInt32();
    var crcBytes = new byte[4];
    _WriteUInt32BE(crcBytes, crcValue);
    stream.Write(crcBytes);
  }

  private static void _WriteUInt32BE(Span<byte> target, uint value) {
    target[0] = (byte)(value >> 24);
    target[1] = (byte)(value >> 16);
    target[2] = (byte)(value >> 8);
    target[3] = (byte)value;
  }
}
