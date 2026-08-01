using System;
using System.Buffers.Binary;
using System.Text;

namespace FileFormat.NokiaLogo;

/// <summary>Assembles Nokia Operator Logo file bytes.</summary>
public static class NokiaLogoWriter {

  public static byte[] ToBytes(NokiaLogoFile file) {
    var pixels = file.Width * file.Height;
    var result = new byte[NokiaLogoFile.HeaderSize + pixels];

    Encoding.ASCII.GetBytes(NokiaLogoFile.Signature).CopyTo(result, 0);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(NokiaLogoFile.WidthOffset), (ushort)file.Width);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(NokiaLogoFile.HeightOffset), (ushort)file.Height);

    // A character a pixel, which is what makes the body legible in a text editor.
    var data = file.PixelData ?? [];
    for (var i = 0; i < pixels; ++i)
      result[NokiaLogoFile.HeaderSize + i] = (byte)(i < data.Length && data[i] != 0 ? '1' : '0');

    return result;
  }
}
