using System;
using System.IO;

namespace FileFormat.GraphicsMaster;

/// <summary>Reads Graphics Master files from bytes, streams, or file paths.</summary>
public static class GraphicsMasterReader {

  public static GraphicsMasterFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Graphics Master file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static GraphicsMasterFile FromStream(Stream stream) {
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

  public static GraphicsMasterFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static GraphicsMasterFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != GraphicsMasterFile.ExpectedFileSize)
      throw new InvalidDataException($"Graphics Master file must be exactly {GraphicsMasterFile.ExpectedFileSize} bytes, got {data.Length}.");

    var pixelData = new byte[GraphicsMasterFile.ExpectedFileSize];
    data.AsSpan(0, GraphicsMasterFile.ExpectedFileSize).CopyTo(pixelData);

    return new GraphicsMasterFile { PixelData = pixelData };
  }
}
