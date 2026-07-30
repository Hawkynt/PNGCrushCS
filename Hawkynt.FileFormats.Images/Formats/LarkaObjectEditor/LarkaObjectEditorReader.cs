using System;
using System.IO;

namespace FileFormat.LarkaObjectEditor;

/// <summary>Reads Larka Edytor Obiektów pictures from bytes, streams, or file paths.</summary>
public static class LarkaObjectEditorReader {

  public static LarkaObjectEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Object not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static LarkaObjectEditorFile FromStream(Stream stream) {
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

  public static LarkaObjectEditorFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != LarkaObjectEditorFile.FileSize)
      throw new InvalidDataException($"An object is {LarkaObjectEditorFile.FileSize} bytes, got {data.Length}.");

    return new() { Data = data.ToArray() };
  }

  public static LarkaObjectEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
