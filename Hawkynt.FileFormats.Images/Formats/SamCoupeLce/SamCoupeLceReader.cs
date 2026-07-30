using System;
using System.IO;

namespace FileFormat.SamCoupeLce;

/// <summary>Reads SAM Coupe interlaced pictures from bytes, streams, or file paths.</summary>
public static class SamCoupeLceReader {

  public static SamCoupeLceFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Interlaced picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SamCoupeLceFile FromStream(Stream stream) {
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

  public static SamCoupeLceFile FromSpan(ReadOnlySpan<byte> data) {
    // The first screen's interrupt list says where it ends, and that is where the second begins;
    // nothing else in the file gives either length away.
    var second = _EndOfScreen(data, 0);
    if (second < 0)
      throw new InvalidDataException("Not an interlaced picture: the first screen's interrupt list does not terminate.");

    var end = _EndOfScreen(data, second);
    if (end != data.Length)
      throw new InvalidDataException(
        $"Not an interlaced picture: the second screen ends at {end} rather than {data.Length}.");

    return new() { Data = data.ToArray(), SecondScreenOffset = second };
  }

  /// <summary>Where a screen's interrupt list finishes, or -1 when it runs off the end.</summary>
  private static int _EndOfScreen(ReadOnlySpan<byte> data, int screen) {
    var offset = screen + SamCoupeLceFile.InterruptOffset;
    if (offset < 0 || offset >= data.Length)
      return -1;

    while (data[offset] != SamCoupeLceFile.InterruptTerminator) {
      offset += SamCoupeLceFile.InterruptRecordSize;
      if (offset >= data.Length)
        return -1;
    }

    return offset + 1;
  }

  public static SamCoupeLceFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
