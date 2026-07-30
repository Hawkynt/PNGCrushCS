using System;
using System.IO;

namespace FileFormat.GraphSaurusInterlaced;

/// <summary>Reads Graph Saurus interlaced pictures from bytes, streams, or file paths.</summary>
public static class GraphSaurusInterlacedReader {

  public static GraphSaurusInterlacedFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Graph Saurus picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GraphSaurusInterlacedFile FromStream(Stream stream) {
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

  public static GraphSaurusInterlacedFile FromSpan(ReadOnlySpan<byte> data) {
    // No header of any kind; the size is the whole identification.
    if (data.Length != GraphSaurusInterlacedFile.FileSize)
      throw new InvalidDataException(
        $"A Graph Saurus interlaced picture is {GraphSaurusInterlacedFile.FileSize} bytes, got {data.Length}.");

    return new() { PixelData = data.ToArray() };
  }

  public static GraphSaurusInterlacedFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
