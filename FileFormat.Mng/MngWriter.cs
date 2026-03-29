using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;

namespace FileFormat.Mng;

/// <summary>Assembles MNG file bytes from an MNG data model.</summary>
public static class MngWriter {

  private static readonly byte[] _MNG_SIGNATURE = { 0x8A, 0x4D, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
  private static readonly byte[] _PNG_SIGNATURE = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

  public static byte[] ToBytes(MngFile file) {
    ArgumentNullException.ThrowIfNull(file);

    using var ms = new MemoryStream();

    // MNG signature
    ms.Write(_MNG_SIGNATURE);

    // MHDR chunk
    var mhdrData = new byte[MngHeader.StructSize];
    var mhdr = new MngHeader(
      (uint)file.Width,
      (uint)file.Height,
      (uint)file.TicksPerSecond,
      0,
      (uint)file.Frames.Count,
      0,
      1 // VLC profile
    );
    mhdr.WriteTo(mhdrData);
    _WriteChunk(ms, "MHDR", mhdrData);

    // TERM chunk
    var termData = new byte[10];
    termData[0] = (byte)file.TermAction;
    // bytes 1: after-term action (0 = show indicated frame)
    // bytes 2-5: delay (0)
    // bytes 6-9: iteration count
    _WriteUInt32BE(termData.AsSpan(6), (uint)file.NumPlays);
    _WriteChunk(ms, "TERM", termData);

    // Embedded PNG frames (strip PNG signature, write bare chunks)
    foreach (var frame in file.Frames) {
      if (frame.Length < _PNG_SIGNATURE.Length)
        continue;

      // Strip 8-byte PNG signature, write remaining chunk stream
      var offset = _PNG_SIGNATURE.Length;
      while (offset + 8 <= frame.Length) {
        var chunkLength = (int)_ReadUInt32BE(frame.AsSpan(offset));
        var totalChunkSize = 4 + 4 + chunkLength + 4;
        if (offset + totalChunkSize > frame.Length)
          break;

        ms.Write(frame, offset, totalChunkSize);
        offset += totalChunkSize;
      }
    }

    // MEND chunk
    _WriteChunk(ms, "MEND", []);

    return ms.ToArray();
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

    // CRC32 over type + data
    var crc = new Crc32();
    crc.Append(typeBytes);
    if (data.Length > 0)
      crc.Append(data);

    var crcValue = crc.GetCurrentHashAsUInt32();
    // CRC in MNG/PNG is big-endian
    var crcBytes = new byte[4];
    _WriteUInt32BE(crcBytes, crcValue);
    stream.Write(crcBytes);
  }

  private static uint _ReadUInt32BE(ReadOnlySpan<byte> data)
    => (uint)(data[0] << 24 | data[1] << 16 | data[2] << 8 | data[3]);

  private static void _WriteUInt32BE(Span<byte> target, uint value) {
    target[0] = (byte)(value >> 24);
    target[1] = (byte)(value >> 16);
    target[2] = (byte)(value >> 8);
    target[3] = (byte)value;
  }
}
