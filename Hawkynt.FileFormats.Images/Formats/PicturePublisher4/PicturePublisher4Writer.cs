using System;
using System.Buffers.Binary;
using FileFormat.Tiff;

namespace FileFormat.PicturePublisher4;

/// <summary>Writes the verified Picture Publisher 4 form containing one complete TIFF.</summary>
public static class PicturePublisher4Writer {

  public static byte[] ToBytes(PicturePublisher4File file) {
    if (file.Embedded == null || file.Embedded.Length < 8)
      throw new ArgumentException("Picture Publisher 4 requires a complete TIFF payload.", nameof(file));

    var offset = Math.Max(PicturePublisher4File.MinFileSize, file.PictureOffset);
    var output = new byte[checked(offset + file.Embedded.Length)];
    PicturePublisher4File.Signature.CopyTo(output);
    BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(PicturePublisher4File.PointerOffset, 4), offset);
    file.Embedded.CopyTo(output, offset);
    return output;
  }
}
