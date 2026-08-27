using System;
using System.Buffers.Binary;

namespace FileFormat.MegaluxFrame;

/// <summary>Writes Megalux Frame pictures (.frm) in the layout accepted by XnView and this reader.</summary>
public static class MegaluxFrameWriter {

  public static byte[] ToBytes(MegaluxFrameFile file) {
    if (file.Width is < 1 or > ushort.MaxValue || file.Height is < 1 or > ushort.MaxValue)
      throw new ArgumentOutOfRangeException(nameof(file), "Megalux Frame dimensions must fit in unsigned 16-bit words.");

    var count = checked(file.Width * file.Height);
    var required = checked(count * 3);
    if (file.PixelData == null || file.PixelData.Length < required)
      throw new ArgumentException("The Megalux Frame image does not contain enough RGB pixel data for its dimensions.", nameof(file));

    var result = new byte[checked(MegaluxFrameFile.PixelDataOffset + count * MegaluxFrameFile.BytesPerPixel)];
    MegaluxFrameFile.Signature.CopyTo(result);
    result[3] = MegaluxFrameFile.SupportedFormatCode;
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(4), checked((ushort)file.Width));
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(6), checked((ushort)file.Height));

    var from = 0;
    var to = MegaluxFrameFile.PixelDataOffset;
    for (var i = 0; i < count; ++i, from += 3, to += MegaluxFrameFile.BytesPerPixel) {
      result[to] = file.PixelData[from + 2];
      result[to + 1] = file.PixelData[from + 1];
      result[to + 2] = file.PixelData[from];
      result[to + 3] = 0;
    }

    return result;
  }
}
