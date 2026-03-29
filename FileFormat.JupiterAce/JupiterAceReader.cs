using System;
using System.IO;

namespace FileFormat.JupiterAce;

/// <summary>Reads Jupiter Ace character screen files from bytes, streams, or file paths.</summary>
public static class JupiterAceReader {

  public static JupiterAceFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("JupiterAce file not found.", file.FullName);
    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static JupiterAceFile FromStream(Stream stream) {
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

  public static JupiterAceFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != JupiterAceFile.FileSize)
      throw new InvalidDataException($"Invalid JupiterAce data size: expected exactly {JupiterAceFile.FileSize} bytes, got {data.Length}.");

    var pixelData = new byte[JupiterAceFile.FileSize];
    data.AsSpan(0, JupiterAceFile.FileSize).CopyTo(pixelData);
    return new() { PixelData = pixelData };
  }
}
