using System;
using System.IO;

namespace FileFormat.LastWordFont;

/// <summary>Reads The Last Word font (.f80) files from bytes, streams, or file paths.</summary>
public static class LastWordFontReader {

  public static LastWordFontFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Font file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static LastWordFontFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static LastWordFontFile FromSpan(ReadOnlySpan<byte> data) {
    // No header of any kind; the fixed size is the only thing identifying it.
    if (data.Length != LastWordFontFile.FileSize)
      throw new InvalidDataException($"A Last Word font is exactly {LastWordFontFile.FileSize} bytes, got {data.Length}.");

    return new() { GlyphData = data.ToArray() };
  }

  public static LastWordFontFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
