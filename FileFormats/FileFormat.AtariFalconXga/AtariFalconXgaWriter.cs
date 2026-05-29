using System;

namespace FileFormat.AtariFalconXga;

/// <summary>Assembles Atari Falcon XGA 16-bit true color file bytes from pixel data.</summary>
public static class AtariFalconXgaWriter {

  public static byte[] ToBytes(AtariFalconXgaFile file) => Assemble(file.PixelData, file.Width, file.Height);

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var expectedPixelBytes = width * height * 2;
    var result = new byte[AtariFalconXgaHeader.StructSize + expectedPixelBytes];

    var header = new AtariFalconXgaHeader((ushort)width, (ushort)height);
    header.WriteTo(result.AsSpan());

    var copyLen = Math.Min(expectedPixelBytes, pixelData.Length);
    pixelData.AsSpan(0, copyLen).CopyTo(result.AsSpan(AtariFalconXgaHeader.StructSize));

    return result;
  }
}
