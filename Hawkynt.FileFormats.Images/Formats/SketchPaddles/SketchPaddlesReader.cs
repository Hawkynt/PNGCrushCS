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

  public static SketchPaddlesFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

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
