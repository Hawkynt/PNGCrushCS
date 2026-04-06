using System;
using System.IO;

namespace FileFormat.AtariMaxi;

/// <summary>Reads Maxi files from bytes, streams, or file paths.</summary>
public static class AtariMaxiReader {

  public static AtariMaxiFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Maxi file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static AtariMaxiFile FromStream(Stream stream) {
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

  public static AtariMaxiFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static AtariMaxiFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length != AtariMaxiFile.ExpectedFileSize)
      throw new InvalidDataException($"Maxi file must be exactly {AtariMaxiFile.ExpectedFileSize} bytes, got {data.Length}.");

    var pixelData = new byte[AtariMaxiFile.ExpectedFileSize];
    data.AsSpan(0, AtariMaxiFile.ExpectedFileSize).CopyTo(pixelData);

    return new AtariMaxiFile { PixelData = pixelData };
  }
}
