using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Spiff;

public static class SpiffWriter {

  public static byte[] ToBytes(SpiffFile file) {
    ArgumentNullException.ThrowIfNull(file.CompressedPayload);

    // APP8 body: "SPIFF\0" (6) + directory header (24) = 30 bytes. App8 length field is 2 bytes
    // and includes itself ⇒ length = 2 + 30 = 32.
    // Directory bytes: ver(2) + profile(1) + components(1) + height(4) + width(4)
    //                + colourSpace(1) + bps(1) + compression(1) + resUnits(1) + vRes(4) + hRes(4) = 24
    const int app8Length = 32;
    using var ms = new MemoryStream();
    using var w = new BinaryWriter(ms);

    w.Write((byte)0xFF); w.Write((byte)0xD8); // SOI
    w.Write((byte)0xFF); w.Write((byte)0xE8); // APP8
    Span<byte> len = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16BigEndian(len, app8Length);
    w.Write(len);

    // "SPIFF\0"
    w.Write((byte)'S'); w.Write((byte)'P'); w.Write((byte)'I'); w.Write((byte)'F'); w.Write((byte)'F'); w.Write((byte)0);

    // Directory header (30 bytes):
    w.Write((byte)2); w.Write((byte)0);          // version 2.0
    w.Write(file.ProfileId);
    w.Write(file.ComponentCount);
    Span<byte> u32 = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(u32, (uint)file.Height); w.Write(u32);
    BinaryPrimitives.WriteUInt32BigEndian(u32, (uint)file.Width);  w.Write(u32);
    w.Write(file.ColorSpace);
    w.Write(file.BitsPerSample);
    w.Write(file.CompressionType);
    w.Write((byte)0); // resolution units: 0 = aspect-ratio only
    BinaryPrimitives.WriteUInt32BigEndian(u32, 1u); w.Write(u32); // vertical res
    BinaryPrimitives.WriteUInt32BigEndian(u32, 1u); w.Write(u32); // horizontal res

    w.Write(file.CompressedPayload);
    return ms.ToArray();
  }
}
