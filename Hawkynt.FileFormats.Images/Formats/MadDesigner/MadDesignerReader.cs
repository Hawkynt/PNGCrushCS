using System;
using System.IO;

namespace FileFormat.MadDesigner;

/// <summary>Reads Mad Designer pictures from bytes, streams, or file paths.</summary>
public static class MadDesignerReader {

  public static MadDesignerFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Mad Designer picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MadDesignerFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static MadDesignerFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != MadDesignerFile.FileSize)
      throw new InvalidDataException($"A Mad Designer picture is {MadDesignerFile.FileSize} bytes, got {data.Length}.");

    return new() { BitmapData = data.ToArray() };
  }

  public static MadDesignerFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
