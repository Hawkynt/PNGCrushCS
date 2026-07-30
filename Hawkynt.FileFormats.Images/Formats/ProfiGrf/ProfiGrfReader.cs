using System;
using System.IO;

namespace FileFormat.ProfiGrf;

/// <summary>Reads Profi pictures from bytes, streams, or file paths.</summary>
public static class ProfiGrfReader {

  public static ProfiGrfFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ProfiGrfFile FromStream(Stream stream) {
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

  public static ProfiGrfFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != ProfiGrfFile.FileSize)
      throw new InvalidDataException($"A Profi picture is {ProfiGrfFile.FileSize} bytes, got {data.Length}.");

    if (!data[..ProfiGrfFile.Signature.Length].SequenceEqual(ProfiGrfFile.Signature))
      throw new InvalidDataException("Not a Profi picture: wrong header.");

    return new() { Data = data.ToArray() };
  }

  public static ProfiGrfFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
