using System;
using System.Buffers.Binary;

namespace FileFormat.CoreIdc;

/// <summary>Writes Core IDC pictures as three eight-bit band-sequential colour planes.</summary>
public static class CoreIdcWriter {

  public static byte[] ToBytes(CoreIdcFile file) {
    if (file.Width < 1 || file.Height < 1)
      throw new ArgumentException($"Invalid Core IDC dimensions {file.Width}x{file.Height}.", nameof(file));
    if (file.Planes != 3 || file.BitsPerPixel != 8)
      throw new ArgumentException("The writer emits Core IDC as three 8-bit colour planes.", nameof(file));

    var planeSize = checked(file.Width * file.Height);
    var expected = checked(planeSize * 3);
    if (file.PixelData == null || file.PixelData.Length < expected)
      throw new ArgumentException($"Core IDC needs {expected} plane bytes.", nameof(file));

    var output = new byte[checked(expected + CoreIdcFile.TrailerSize)];
    file.PixelData.AsSpan(0, expected).CopyTo(output);
    var trailer = output.AsSpan(expected, CoreIdcFile.TrailerSize);
    BinaryPrimitives.WriteUInt32BigEndian(trailer, (uint)file.Width);
    BinaryPrimitives.WriteUInt32BigEndian(trailer[4..], (uint)file.Height);
    BinaryPrimitives.WriteUInt16BigEndian(trailer[8..], 3);
    BinaryPrimitives.WriteUInt16BigEndian(trailer[10..], 8);
    CoreIdcFile.Signature.CopyTo(trailer[(CoreIdcFile.TrailerSize - CoreIdcFile.SignatureFromEnd)..]);
    return output;
  }
}
