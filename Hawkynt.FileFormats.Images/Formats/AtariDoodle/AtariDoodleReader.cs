using System;
using System.IO;

namespace FileFormat.AtariDoodle;

/// <summary>Reads original Atari ST Doodle (.DOO) monochrome screen dumps.</summary>
public static class AtariDoodleReader {

  /// <summary>Reads a Doodle screen from disk.</summary>
  public static AtariDoodleFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Atari ST Doodle file not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  /// <summary>Reads exactly one Doodle screen from the current stream position through end-of-stream.</summary>
  public static AtariDoodleFile FromStream(Stream stream) {
    ArgumentNullException.ThrowIfNull(stream);
    if (stream.CanSeek) {
      var length = checked((int)(stream.Length - stream.Position));
      if (length != AtariDoodleFile.ScreenDataSize)
        throw new InvalidDataException($"Atari ST Doodle files must be exactly {AtariDoodleFile.ScreenDataSize} bytes; got {length}.");

      var data = new byte[length];
      stream.ReadExactly(data);
      return FromSpan(data);
    }

    using var ms = new MemoryStream();
    stream.CopyTo(ms);
    return FromSpan(ms.ToArray());
  }

  /// <summary>Parses one complete 32,000-byte high-resolution screen dump.</summary>
  public static AtariDoodleFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length != AtariDoodleFile.ScreenDataSize)
      throw new InvalidDataException($"Atari ST Doodle files must be exactly {AtariDoodleFile.ScreenDataSize} bytes; got {data.Length}.");

    return new AtariDoodleFile { ScreenData = data.ToArray() };
  }

  /// <summary>Parses one complete Doodle byte array.</summary>
  public static AtariDoodleFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
