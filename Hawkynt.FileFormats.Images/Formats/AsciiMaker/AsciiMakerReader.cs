using System;
using System.IO;

namespace FileFormat.AsciiMaker;

/// <summary>Reads ASCII maker screens from bytes, streams, or file paths.</summary>
public static class AsciiMakerReader {

  public static AsciiMakerFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("ASCII maker screen not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AsciiMakerFile FromStream(Stream stream) {
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

  public static AsciiMakerFile FromSpan(ReadOnlySpan<byte> data) {
    // Either the grid exactly, or the grid padded out to a whole page; there is no header to check.
    if (data.Length != AsciiMakerFile.ScreenSize && data.Length != AsciiMakerFile.PaddedSize)
      throw new InvalidDataException(
        $"An ASCII maker screen is {AsciiMakerFile.ScreenSize} or {AsciiMakerFile.PaddedSize} bytes, got {data.Length}.");

    return new() { Characters = data[..AsciiMakerFile.ScreenSize].ToArray() };
  }

  public static AsciiMakerFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
