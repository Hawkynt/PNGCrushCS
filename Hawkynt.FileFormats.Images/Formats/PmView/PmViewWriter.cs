using System;
using System.Buffers.Binary;

namespace FileFormat.PmView;

/// <summary>Assembles a PM picture: the header, the bands one plane at a time, then the comment.</summary>
public static class PmViewWriter {

  public static byte[] ToBytes(PmViewFile file) {
    var bands = file.Bands is 1 or 3 ? file.Bands : 3;
    var plane = file.Width * file.Height;
    var pixels = file.PixelData ?? [];
    var comment = file.Comment ?? [];

    var result = new byte[PmViewFile.HeaderSize + plane * bands + comment.Length];
    PmViewFile.Magic.CopyTo(result);
    BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(4), bands);
    BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(8), file.Height);
    BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(12), file.Width);
    BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(16), 1);
    BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(20), PmViewFile.UnsignedByteForm);
    BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(24), comment.Length);

    for (var band = 0; band < bands; ++band)
    for (var i = 0; i < plane; ++i) {
      var source = i * bands + band;
      if (source < pixels.Length)
        result[PmViewFile.HeaderSize + band * plane + i] = pixels[source];
    }

    comment.CopyTo(result.AsSpan(PmViewFile.HeaderSize + plane * bands));

    return result;
  }
}
