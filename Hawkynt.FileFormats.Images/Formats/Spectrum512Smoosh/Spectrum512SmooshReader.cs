using System;
using System.IO;

namespace FileFormat.Spectrum512Smoosh;

/// <summary>Reads Spectrum 512 Smooshed (SPS) files from bytes, streams, or file paths.</summary>
public static class Spectrum512SmooshReader {

  public static Spectrum512SmooshFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Spectrum 512 Smooshed file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Spectrum512SmooshFile FromStream(Stream stream) {
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

  public static Spectrum512SmooshFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < Spectrum512SmooshFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid SPS file: expected at least {Spectrum512SmooshFile.MinFileSize} bytes, got {data.Length}.");

    var rawData = new byte[data.Length];
    data.Slice(0, data.Length).CopyTo(rawData);

    return new Spectrum512SmooshFile {
      RawData = rawData
    };
    }

  public static Spectrum512SmooshFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    if (data.Length < Spectrum512SmooshFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid SPS file: expected at least {Spectrum512SmooshFile.MinFileSize} bytes, got {data.Length}.");

    var rawData = new byte[data.Length];
    data.AsSpan(0, data.Length).CopyTo(rawData);

    return new Spectrum512SmooshFile {
      RawData = rawData
    };
  }
}
