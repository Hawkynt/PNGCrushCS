using System;
using System.Buffers.Binary;

namespace FileFormat.IbmKips;

/// <summary>Assembles a KIPS picture: the signature, the size, then a byte a pixel.</summary>
public static class IbmKipsWriter {

  public static byte[] ToBytes(IbmKipsFile file) {
    var pixels = file.PixelData ?? [];
    var result = new byte[IbmKipsFile.HeaderSize + file.Width * file.Height];

    (file.Header ?? []).AsSpan(0, Math.Min((file.Header ?? []).Length, IbmKipsFile.HeaderSize)).CopyTo(result);
    IbmKipsFile.Magic.CopyTo(result);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(IbmKipsFile.HeightAt), (ushort)file.Height);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(IbmKipsFile.WidthAt), (ushort)file.Width);
    pixels.AsSpan(0, Math.Min(pixels.Length, file.Width * file.Height))
      .CopyTo(result.AsSpan(IbmKipsFile.HeaderSize));

    return result;
  }
}
