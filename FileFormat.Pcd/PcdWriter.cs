using System;
using System.Buffers.Binary;

namespace FileFormat.Pcd;

/// <summary>Assembles PCD (Kodak Photo CD) file bytes from pixel data.</summary>
public static class PcdWriter {

  public static byte[] ToBytes(PcdFile file) {
    ArgumentNullException.ThrowIfNull(file);
    return Assemble(file.PixelData, file.Width, file.Height);
  }

  internal static byte[] Assemble(byte[] pixelData, int width, int height) {
    var expectedPixelBytes = width * height * 3;
    var result = new byte[PcdFile.HeaderSize + expectedPixelBytes];

    PcdFile.Magic.AsSpan(0, PcdFile.Magic.Length).CopyTo(result.AsSpan(PcdFile.PreambleSize));

    var dimOffset = PcdFile.PreambleSize + PcdFile.Magic.Length;
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(dimOffset), (ushort)width);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(dimOffset + 2), (ushort)height);

    var copyLen = Math.Min(expectedPixelBytes, pixelData.Length);
    pixelData.AsSpan(0, copyLen).CopyTo(result.AsSpan(PcdFile.HeaderSize));

    return result;
  }
}
