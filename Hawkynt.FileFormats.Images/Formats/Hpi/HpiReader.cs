using System;
using System.Buffers.Binary;
using System.IO;

namespace FileFormat.Hpi;

/// <summary>Reads Hemera photo-objects from bytes, streams, or file paths.</summary>
public static class HpiReader {

  public static HpiFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Hemera photo-object not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HpiFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var data = new byte[stream.Length - stream.Position];
      stream.ReadExactly(data);
      return FromBytes(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromBytes(ms.ToArray());
  }

  public static HpiFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < HpiFile.JpegOffsetField + 4)
      throw new InvalidDataException($"Data too small for a Hemera photo-object (got {data.Length} bytes).");

    if (!data[..HpiFile.Magic.Length].SequenceEqual(HpiFile.Magic))
      throw new InvalidDataException("Not a Hemera photo-object: it does not open with the HPI signature.");

    var jpegOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[HpiFile.JpegOffsetField..]);
    if (jpegOffset < 0 || jpegOffset + 3 >= data.Length)
      throw new InvalidDataException($"A Hemera photo-object states its picture at {jpegOffset}, which is past the end of a {data.Length}-byte file.");

    if (data[jpegOffset] != 0xFF || data[jpegOffset + 1] != 0xD8)
      throw new InvalidDataException($"A Hemera photo-object carries a JPEG at the offset it states; this file has none at {jpegOffset}.");

    return new() { Embedded = data[jpegOffset..].ToArray() };
  }

  public static HpiFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
