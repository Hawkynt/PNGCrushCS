using System;
using System.IO;

namespace FileFormat.HcbEditor;

/// <summary>Reads HCB-editor pictures from bytes, streams, or file paths.</summary>
public static class HcbEditorReader {

  public static HcbEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("HCB picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static HcbEditorFile FromStream(Stream stream) {
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

  public static HcbEditorFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != HcbEditorFile.FileSize)
      throw new InvalidDataException($"An HCB picture is {HcbEditorFile.FileSize} bytes, got {data.Length}.");

    return new() { Data = data.ToArray() };
  }

  public static HcbEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
