using System;
using System.IO;

namespace FileFormat.Gif;

/// <summary>Convenience alias for <see cref="GifReader"/> matching the external
/// <c>Hawkynt.GifFileFormat.Reader</c> API so consumers can migrate by namespace swap.</summary>
public static class Reader {
  public static GifFile FromFile(FileInfo file) => GifReader.FromFile(file);
  public static GifFile FromBytes(byte[] data) => GifReader.FromBytes(data);
  public static GifFile FromSpan(ReadOnlySpan<byte> data) => GifReader.FromSpan(data);
  public static GifFile FromStream(Stream stream) => GifReader.FromStream(stream);
}
