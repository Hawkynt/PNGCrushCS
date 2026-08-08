using System;
using System.Buffers.Binary;

namespace FileFormat.EccHeader;

/// <summary>Assembles an ECC picture: the header, then the PNG.</summary>
public static class EccHeaderWriter {

  public static byte[] ToBytes(EccHeaderFile file) {
    var embedded = file.Embedded ?? [];
    var result = new byte[EccHeaderFile.DefaultPictureOffset + embedded.Length];
    EccHeaderFile.Magic.CopyTo(result);

    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(EccHeaderFile.WidthAt), (ushort)file.Width);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(EccHeaderFile.HeightAt), (ushort)file.Height);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(EccHeaderFile.SecondWidthAt), (ushort)file.Width);
    BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(EccHeaderFile.SecondHeightAt), (ushort)file.Height);

    embedded.CopyTo(result.AsSpan(EccHeaderFile.DefaultPictureOffset));

    return result;
  }
}
