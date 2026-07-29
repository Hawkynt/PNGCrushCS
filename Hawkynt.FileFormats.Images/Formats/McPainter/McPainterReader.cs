using System;
using System.IO;

namespace FileFormat.McPainter;

/// <summary>Reads McPainter pictures from bytes, streams, or file paths.</summary>
public static class McPainterReader {

  public static McPainterFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("McPainter picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static McPainterFile FromStream(Stream stream) {
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

  public static McPainterFile FromSpan(ReadOnlySpan<byte> data) {
    // There is no header at all: the length is the only thing identifying the format.
    if (data.Length != McPainterFile.FileSize)
      throw new InvalidDataException($"A McPainter picture is {McPainterFile.FileSize} bytes, got {data.Length}.");

    return new() { Data = data.ToArray() };
  }

  public static McPainterFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
