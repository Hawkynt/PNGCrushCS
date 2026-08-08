using System;
using System.Buffers.Binary;

namespace FileFormat.Hpi;

/// <summary>Assembles a photo-object: the signature, the offset of the picture, then the picture.</summary>
public static class HpiWriter {

  public static byte[] ToBytes(HpiFile file) {
    var embedded = file.Embedded ?? [];
    var result = new byte[HpiFile.DefaultJpegOffset + embedded.Length];

    HpiFile.Magic.CopyTo(result);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(HpiFile.JpegOffsetField), (uint)HpiFile.DefaultJpegOffset);
    embedded.CopyTo(result.AsSpan(HpiFile.DefaultJpegOffset));

    return result;
  }
}
