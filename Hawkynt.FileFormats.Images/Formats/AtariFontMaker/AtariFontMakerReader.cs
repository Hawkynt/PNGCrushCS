using System;
using System.IO;

namespace FileFormat.AtariFontMaker;

/// <summary>Reads Atari FontMaker double character sets from bytes, streams, or file paths.</summary>
public static class AtariFontMakerReader {

  public static AtariFontMakerFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Character set not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariFontMakerFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static AtariFontMakerFile FromSpan(ReadOnlySpan<byte> data) {
    // Two character sets and nothing else; the length is the whole identification.
    if (data.Length != AtariFontMakerFile.FileSize)
      throw new InvalidDataException($"A double character set is {AtariFontMakerFile.FileSize} bytes, got {data.Length}.");

    return new() { GlyphData = data.ToArray() };
  }

  public static AtariFontMakerFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
