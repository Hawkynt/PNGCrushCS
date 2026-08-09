using System;
using System.IO;

namespace FileFormat.Portrait;

/// <summary>Reads Portrait pictures (.cvp) from bytes, streams, or file paths.</summary>
public static class PortraitReader {

  public static PortraitFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Portrait picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static PortraitFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var buffer = new byte[stream.Length - stream.Position];
      stream.ReadExactly(buffer);
      return FromBytes(buffer);
    }

    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return FromBytes(memory.ToArray());
  }

  public static PortraitFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static PortraitFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != PortraitFile.FileSize)
      throw new InvalidDataException($"A Portrait picture is exactly {PortraitFile.FileSize} bytes and this is {data.Length}; the length is the only thing that tells this format apart.");

    return new() { PlaneData = data.ToArray() };
  }
}
