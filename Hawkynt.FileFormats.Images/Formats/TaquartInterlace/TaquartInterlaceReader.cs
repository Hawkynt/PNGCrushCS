using System;
using System.IO;

namespace FileFormat.TaquartInterlace;

/// <summary>Reads Taquart Interlace Pictures from bytes, streams, or file paths.</summary>
public static class TaquartInterlaceReader {

  public static TaquartInterlaceFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static TaquartInterlaceFile FromStream(Stream stream) {
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

  public static TaquartInterlaceFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 129 || !data[..TaquartInterlaceFile.Signature.Length].SequenceEqual(TaquartInterlaceFile.Signature))
      throw new InvalidDataException("Not a Taquart Interlace Picture.");

    int width = data[5], height = data[6];
    if (width > TaquartInterlaceFile.MaxStoredWidth || (width & 3) != 0 || height > TaquartInterlaceFile.MaxStoredHeight)
      throw new InvalidDataException($"A Taquart picture is not {width}x{height}.");

    // The stored field length has to agree with the dimensions, and all three fields must fit.
    var fieldLength = data[7] | (data[8] << 8);
    if (fieldLength != (width >> 2) * height || data.Length != TaquartInterlaceFile.FieldsOffset + 3 * fieldLength)
      throw new InvalidDataException($"A {width}x{height} picture does not occupy {data.Length} bytes.");

    return new() { Data = data.ToArray(), StoredWidth = width, StoredHeight = height, FieldLength = fieldLength };
  }

  public static TaquartInterlaceFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
