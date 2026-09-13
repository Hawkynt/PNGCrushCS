using System;
using System.IO;

namespace FileFormat.AtariFont;

/// <summary>Reads Atari 8-bit character sets from bytes, streams, or file paths.</summary>
public static class AtariFontReader {

  public static AtariFontFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari font file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariFontFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static AtariFontFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length != AtariFontFile.FileSize)
      throw new InvalidDataException($"Invalid Atari font data size: expected exactly {AtariFontFile.FileSize} bytes, got {data.Length}.");

    var fontData = new byte[AtariFontFile.FileSize];
    data.Slice(0, AtariFontFile.FileSize).CopyTo(fontData);

    return new AtariFontFile { FontData = fontData };
    }

  public static AtariFontFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
