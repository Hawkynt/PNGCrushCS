using System;
using System.IO;

namespace FileFormat.StarPainterFont;

/// <summary>Reads Star Painter character sets from bytes, streams, or file paths.</summary>
public static class StarPainterFontReader {

  public static StarPainterFontFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Character set not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static StarPainterFontFile FromStream(Stream stream) {
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

  public static StarPainterFontFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != StarPainterFontFile.FileSize)
      throw new InvalidDataException(
        $"A Star Painter character set is {StarPainterFontFile.FileSize} bytes, got {data.Length}.");

    if (!data[..StarPainterFontFile.Signature.Length].SequenceEqual(StarPainterFontFile.Signature))
      throw new InvalidDataException("Not a Star Painter character set: wrong load address.");

    return new() { Data = data.ToArray() };
  }

  public static StarPainterFontFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
