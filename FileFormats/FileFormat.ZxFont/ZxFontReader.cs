using System;
using System.IO;

namespace FileFormat.ZxFont;

/// <summary>Reads ZX Spectrum character sets from bytes, streams, or file paths.</summary>
public static class ZxFontReader {

  public static ZxFontFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Character set file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ZxFontFile FromStream(Stream stream) {
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

  public static ZxFontFile FromSpan(ReadOnlySpan<byte> data) {
    // No header of any kind; a whole number of eight-byte glyphs is the only constraint.
    if (data.Length == 0 || data.Length % ZxFontFile.GlyphHeight != 0)
      throw new InvalidDataException(
        $"A character set is a whole number of {ZxFontFile.GlyphHeight}-byte glyphs, got {data.Length} bytes.");

    return new() { GlyphData = data.ToArray() };
  }

  public static ZxFontFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
