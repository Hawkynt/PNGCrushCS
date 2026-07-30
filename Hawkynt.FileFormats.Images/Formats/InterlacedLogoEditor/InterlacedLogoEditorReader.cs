using System;
using System.IO;

namespace FileFormat.InterlacedLogoEditor;

/// <summary>Reads Interlaced Logo Editor pictures from bytes, streams, or file paths.</summary>
public static class InterlacedLogoEditorReader {

  public static InterlacedLogoEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Logo not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static InterlacedLogoEditorFile FromStream(Stream stream) {
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

  public static InterlacedLogoEditorFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != InterlacedLogoEditorFile.FileSize)
      throw new InvalidDataException($"A logo is {InterlacedLogoEditorFile.FileSize} bytes, got {data.Length}.");

    return new() { Data = data.ToArray() };
  }

  public static InterlacedLogoEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
