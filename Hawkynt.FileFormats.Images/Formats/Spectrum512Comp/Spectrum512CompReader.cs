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

  public static Spectrum512CompFile FromStream(Stream stream) => FromBytes(StreamBytes.ReadAll(stream));

  public static Spectrum512CompFile FromSpan(ReadOnlySpan<byte> data) {

    if (data.Length < Spectrum512CompFile.MinFileSize)
      throw new InvalidDataException($"Data too small for a valid SPC file: expected at least {Spectrum512CompFile.MinFileSize} bytes, got {data.Length}.");

    var rawData = new byte[data.Length];
    data.Slice(0, data.Length).CopyTo(rawData);

    return new Spectrum512CompFile {
      RawData = rawData
    };
    }

  public static Spectrum512CompFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
