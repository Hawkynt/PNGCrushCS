using System;
using System.IO;

namespace FileFormat.Crd;

/// <summary>Writes PowerCard maker documents carrying one complete JFIF JPEG.</summary>
public static class CrdWriter {

  public static byte[] ToBytes(CrdFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var picture = file.PictureData ?? throw new ArgumentException("A CRD picture is required.", nameof(file));
    if (!_HasJfifStart(picture))
      throw new ArgumentException("A CRD picture must be a JFIF JPEG whose APP0 segment immediately follows the SOI marker.", nameof(file));

    var output = new byte[checked(CrdFile.HeaderSize + picture.Length)];
    CrdFile.Magic.CopyTo(output);
    picture.CopyTo(output, CrdFile.HeaderSize);

    try {
      var parsed = CrdReader.FromSpan(output);
      if (parsed.PictureOffset != CrdFile.HeaderSize || parsed.PictureData.Length != picture.Length)
        throw new ArgumentException("A CRD picture must contain exactly one complete JFIF JPEG without trailing bytes.", nameof(file));
    } catch (InvalidDataException exception) {
      throw new ArgumentException("A CRD picture must be a complete, marker-delimited JFIF JPEG.", nameof(file), exception);
    }

    return output;
  }

  private static bool _HasJfifStart(ReadOnlySpan<byte> picture)
    => picture.Length >= CrdFile.JfifIdentifierOffset + 4
       && picture[0] == 0xFF
       && picture[1] == 0xD8
       && picture[2] == 0xFF
       && picture[3] == 0xE0
       && picture.Slice(CrdFile.JfifIdentifierOffset, 4).SequenceEqual("JFIF"u8);
}
