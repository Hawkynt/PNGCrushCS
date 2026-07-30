using System;
using System.IO;

namespace FileFormat.Fuckpaint;

/// <summary>Reads Fuckpaint pictures from bytes, streams, or file paths.</summary>
public static class FuckpaintReader {

  public static FuckpaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Fuckpaint picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static FuckpaintFile FromStream(Stream stream) {
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

  public static FuckpaintFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != FuckpaintFile.FileSize)
      throw new InvalidDataException($"A Fuckpaint picture is {FuckpaintFile.FileSize} bytes, got {data.Length}.");

    return new() { Data = data.ToArray() };
  }

  public static FuckpaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
