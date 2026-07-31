using System;
using System.IO;

namespace FileFormat.MiniPaint;

/// <summary>Reads MINIPAINT pictures from bytes, streams, or file paths.</summary>
public static class MiniPaintReader {

  public static MiniPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static MiniPaintFile FromStream(Stream stream) {
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

  public static MiniPaintFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != MiniPaintFile.FileSize
        || !data[..MiniPaintFile.Signature.Length].SequenceEqual(MiniPaintFile.Signature))
      throw new InvalidDataException("Not a MINIPAINT picture.");

    return new() { Data = data.ToArray() };
  }

  public static MiniPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
