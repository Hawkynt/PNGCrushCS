using System;
using System.IO;

namespace FileFormat.CentauriLogoEditor;

/// <summary>Reads Centauri Logo-Editor pictures from bytes, streams, or file paths.</summary>
public static class CentauriLogoEditorReader {

  public static CentauriLogoEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Logo not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static CentauriLogoEditorFile FromStream(Stream stream) {
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

  public static CentauriLogoEditorFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != CentauriLogoEditorFile.FileSize)
      throw new InvalidDataException($"A Centauri logo is {CentauriLogoEditorFile.FileSize} bytes, got {data.Length}.");

    return new() { Data = data.ToArray() };
  }

  public static CentauriLogoEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
