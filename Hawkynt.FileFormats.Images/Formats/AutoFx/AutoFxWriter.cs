using System;
using System.Buffers.Binary;

namespace FileFormat.AutoFx;

/// <summary>Assembles an Auto F/X picture: the signature, the offset and length, then the JPEG.</summary>
/// <remarks>
/// What the real files keep between the signature and the picture is the program's own furniture —
/// two fixed JPEGs it shows in its interface — and nothing here knows what else lives there. So this
/// writes the header the reader reads, pads to the offset the single-picture samples use, and puts
/// the picture there. The two longs still add up to the length of the file, which is the one thing
/// about these files that has to be true.
/// </remarks>
public static class AutoFxWriter {

  public static byte[] ToBytes(AutoFxFile file) {
    var embedded = file.Embedded ?? [];

    var offset = file.PictureOffset >= AutoFxFile.PictureLengthAt + 4
      ? file.PictureOffset
      : AutoFxFile.DefaultPictureOffset;

    var result = new byte[offset + embedded.Length];

    AutoFxFile.Magic.CopyTo(result);
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(AutoFxFile.PictureOffsetAt), (uint)offset);
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(AutoFxFile.PictureLengthAt), (uint)embedded.Length);
    embedded.CopyTo(result.AsSpan(offset));

    return result;
  }
}
