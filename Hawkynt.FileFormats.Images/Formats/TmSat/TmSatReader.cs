using System;
using System.IO;

namespace FileFormat.TmSat;

/// <summary>Reads TMSAT-1 frames (.imi) from bytes, streams, or file paths.</summary>
public static class TmSatReader {

  public static TmSatFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("TMSat frame not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static TmSatFile FromStream(Stream stream) {
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

  public static TmSatFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }

  public static TmSatFile FromSpan(ReadOnlySpan<byte> data) {

    // The format has no header, so its length is the whole of the evidence and one length is taken.
    // Anything else under this name is refused rather than drawn at a size nothing in it states.
    if (data.Length != TmSatFile.FileSize)
      throw new InvalidDataException($"A TMSat frame is exactly {TmSatFile.FileSize} bytes, being {TmSatFile.Side} by {TmSatFile.Side} at one byte a pixel, and this is {data.Length}.");

    return new() { PixelData = data.ToArray() };
  }
}
