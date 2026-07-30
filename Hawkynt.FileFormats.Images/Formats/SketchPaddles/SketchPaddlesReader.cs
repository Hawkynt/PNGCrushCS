using System;
using System.IO;

namespace FileFormat.SketchPaddles;

/// <summary>Reads Sketch-PadDles pictures from bytes, streams, or file paths.</summary>
public static class SketchPaddlesReader {

  public static SketchPaddlesFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SketchPaddlesFile FromStream(Stream stream) {
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

  public static SketchPaddlesFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != SketchPaddlesFile.FileSize)
      throw new InvalidDataException(
        $"A Sketch-PadDles picture is {SketchPaddlesFile.FileSize} bytes, got {data.Length}.");

    return new() { Data = data.ToArray() };
  }

  public static SketchPaddlesFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
