using System;
using System.IO;

namespace FileFormat.InterlaceLogoDesigner;

/// <summary>Reads Interlace Logo Designer pictures from bytes, streams, or file paths.</summary>
public static class InterlaceLogoDesignerReader {

  public static InterlaceLogoDesignerFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static InterlaceLogoDesignerFile FromStream(Stream stream) {
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

  public static InterlaceLogoDesignerFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != InterlaceLogoDesignerFile.FileSize)
      throw new InvalidDataException(
        $"An Interlace Logo Designer picture is {InterlaceLogoDesignerFile.FileSize} bytes, got {data.Length}.");

    return new() { Data = data.ToArray() };
  }

  public static InterlaceLogoDesignerFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
