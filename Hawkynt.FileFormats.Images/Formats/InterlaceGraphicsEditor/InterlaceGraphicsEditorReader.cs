using System;
using System.IO;

namespace FileFormat.InterlaceGraphicsEditor;

/// <summary>Reads Interlace Graphics Editor pictures from bytes, streams, or file paths.</summary>
public static class InterlaceGraphicsEditorReader {

  public static InterlaceGraphicsEditorFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static InterlaceGraphicsEditorFile FromStream(Stream stream) {
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

  public static InterlaceGraphicsEditorFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != InterlaceGraphicsEditorFile.FileSize)
      throw new InvalidDataException(
        $"An Interlace Graphics Editor picture is {InterlaceGraphicsEditorFile.FileSize} bytes, got {data.Length}.");

    if (!data[..InterlaceGraphicsEditorFile.Signature.Length].SequenceEqual(InterlaceGraphicsEditorFile.Signature))
      throw new InvalidDataException("Not an Interlace Graphics Editor picture: wrong header.");

    return new() { Data = data.ToArray() };
  }

  public static InterlaceGraphicsEditorFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
