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

  public static MadDesignerFile FromStream(Stream stream) {
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
