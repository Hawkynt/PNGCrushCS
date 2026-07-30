using System;
using System.IO;

namespace FileFormat.TimexGigascreen;

/// <summary>Reads Timex hi-res gigascreen pictures from bytes, streams, or file paths.</summary>
public static class TimexGigascreenReader {

  public static TimexGigascreenFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Gigascreen picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static TimexGigascreenFile FromStream(Stream stream) {
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

  public static TimexGigascreenFile FromSpan(ReadOnlySpan<byte> data) {
    // Two screens of bitmap-plus-colour and nothing else; the length is the only identification.
    if (data.Length != TimexGigascreenFile.FileSize)
      throw new InvalidDataException($"A gigascreen picture is {TimexGigascreenFile.FileSize} bytes, got {data.Length}.");

    return new() { Data = data.ToArray() };
  }

  public static TimexGigascreenFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
