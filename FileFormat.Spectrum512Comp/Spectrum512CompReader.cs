using System;
using System.IO;

namespace FileFormat.Spectrum512Comp;

/// <summary>Reads Spectrum 512 Compressed (SPC) files from bytes, streams, or file paths.</summary>
public static class Spectrum512CompReader {

  public static Spectrum512CompFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Spectrum 512 Compressed file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Spectrum512CompFile FromStream(Stream stream) {
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

  public static Spectrum512CompFile FromSpan(ReadOnlySpan<byte> data) => FromBytes(data.ToArray());

  public static Spectrum512CompFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < Spectrum512CompFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid SPC file: expected at least {Spectrum512CompFile.MinFileSize} bytes, got {data.Length}.");

    var rawData = new byte[data.Length];
    data.AsSpan(0, data.Length).CopyTo(rawData);

    return new Spectrum512CompFile {
      RawData = rawData
    };
  }
}
