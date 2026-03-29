using System;
using System.Buffers.Binary;

namespace FileFormat.Fl32;

/// <summary>Assembles FL32 file bytes from float pixel data.</summary>
public static class Fl32Writer {

  public static byte[] ToBytes(Fl32File file) {
    ArgumentNullException.ThrowIfNull(file);
    var totalFloats = file.Width * file.Height * file.Channels;
    var result = new byte[Fl32File.HeaderSize + totalFloats * 4];

    BinaryPrimitives.WriteUInt32LittleEndian(result, Fl32File.Magic);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4), file.Height);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(8), file.Width);
    BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(12), file.Channels);

    var src = file.PixelData;
    var copyLen = Math.Min(src.Length, totalFloats);
    for (var i = 0; i < copyLen; ++i)
      BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(Fl32File.HeaderSize + i * 4), src[i]);

    return result;
  }
}
