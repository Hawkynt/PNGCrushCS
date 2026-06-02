using System.IO;

namespace FileFormat.Gif;

/// <summary>Convenience alias for <see cref="GifWriter"/> matching the external
/// <c>Hawkynt.GifFileFormat.Writer</c> API so consumers can migrate by namespace swap.</summary>
public static class Writer {
  public static byte[] ToBytes(GifFile file) => GifWriter.ToBytes(file);
  public static void WriteTo(GifFile file, Stream output) => GifWriter.WriteTo(file, output);
}
