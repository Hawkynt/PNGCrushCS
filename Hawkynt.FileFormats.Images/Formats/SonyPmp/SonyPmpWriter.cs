using System;
using System.Buffers.Binary;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.SonyPmp;

/// <summary>Writes Sony DSC-F1 PMP files as the documented 124-byte header followed by JPEG.</summary>
public static class SonyPmpWriter {

  public static byte[] ToBytes(SonyPmpFile file) {
    if (file.Width < 1 || file.Height < 1 || file.PixelData == null || file.PixelData.Length < checked(file.Width * file.Height * 3))
      throw new ArgumentException("Sony PMP needs a complete RGB picture.", nameof(file));

    var raw = new RawImage {
      Width = file.Width,
      Height = file.Height,
      Format = PixelFormat.Rgb24,
      PixelData = file.PixelData,
    };
    var jpeg = JpegWriter.ToBytes(JpegFile.FromRawImage(raw));
    var output = new byte[checked(SonyPmpFile.HeaderSize + jpeg.Length)];
    BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(SonyPmpFile.HeaderSizeOffset, 4), SonyPmpFile.HeaderSize);
    BinaryPrimitives.WriteUInt32BigEndian(output.AsSpan(SonyPmpFile.JpegLengthOffset, 4), checked((uint)jpeg.Length));
    if (file.Width <= ushort.MaxValue && file.Height <= ushort.MaxValue) {
      BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(22, 2), (ushort)file.Width);
      BinaryPrimitives.WriteUInt16BigEndian(output.AsSpan(24, 2), (ushort)file.Height);
    }
    jpeg.CopyTo(output, SonyPmpFile.HeaderSize);
    return output;
  }
}
