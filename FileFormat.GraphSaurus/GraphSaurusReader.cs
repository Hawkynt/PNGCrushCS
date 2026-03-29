using System;
using System.IO;

namespace FileFormat.GraphSaurus;

/// <summary>Reads Graph Saurus image files from bytes, streams, or file paths.</summary>
public static class GraphSaurusReader {

  public static GraphSaurusFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Graph Saurus file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GraphSaurusFile FromStream(Stream stream) {
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

  public static GraphSaurusFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != GraphSaurusFile.ExpectedFileSize)
      throw new InvalidDataException($"Graph Saurus file must be exactly {GraphSaurusFile.ExpectedFileSize} bytes, got {data.Length}.");

    var pixels = new byte[GraphSaurusFile.ExpectedFileSize];
    data.AsSpan(0, GraphSaurusFile.ExpectedFileSize).CopyTo(pixels);

    return new() { PixelData = pixels };
  }
}
