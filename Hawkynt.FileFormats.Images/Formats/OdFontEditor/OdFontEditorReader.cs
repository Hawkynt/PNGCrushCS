using System;
using System.IO;

namespace FileFormat.OdFontEditor;

/// <summary>Reads OD Font Editor character sets from bytes, streams, or file paths.</summary>
public static class OdFontEditorReader {

  public static OdFontEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Character set not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static OdFontEditorFile FromStream(Stream stream) {
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

  public static OdFontEditorFile FromSpan(ReadOnlySpan<byte> data) {
    // Ten-row glyphs, so the length is not a power of two and does not collide with an ordinary set.
    if (data.Length != OdFontEditorFile.FileSize)
      throw new InvalidDataException($"An OD Font Editor set is {OdFontEditorFile.FileSize} bytes, got {data.Length}.");

    return new() { GlyphData = data.ToArray() };
  }

  public static OdFontEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
