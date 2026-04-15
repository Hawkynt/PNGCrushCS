using System;
using System.Buffers.Binary;

namespace FileFormat.Fl32;

/// <summary>Assembles FL32 file bytes from float pixel data.</summary>
public static class Fl32Writer {

  public static byte[] ToBytes(Fl32File file) {
    ArgumentNullException.ThrowIfNull(file);
    var totalFloats = file.Width * file.Height * file.Channels;
    var result = new byte[Fl32Header.StructSize + totalFloats * 4];

    BinaryPrimitives.WriteUInt32LittleEndian(result, Fl32File.Magic);
    new Fl32Header(file.Height, file.Width, file.Channels).WriteTo(result);

    var src = file.PixelData;
    var copyLen = Math.Min(src.Length, totalFloats);
    for (var i = 0; i < copyLen; ++i)
      BinaryPrimitives.WriteSingleLittleEndian(result.AsSpan(Fl32Header.StructSize + i * 4), src[i]);

    return result;
  }
}
