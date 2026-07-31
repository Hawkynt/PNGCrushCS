using System;
using System.IO;

namespace FileFormat.SuperHiresFli;

/// <summary>Reads Super Hires FLI Editor pictures from bytes, streams, or file paths.</summary>
public static class SuperHiresFliReader {

  public static SuperHiresFliFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static SuperHiresFliFile FromStream(Stream stream) {
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

  public static SuperHiresFliFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length == SuperHiresFliFile.WideFileSize)
      return new() { Data = data.ToArray(), HasSprites = true };

    if (data.Length < 6)
      throw new InvalidDataException($"Not a Super Hires FLI picture: {data.Length} bytes.");

    return new() { Data = _Unpack(data), HasSprites = false };
  }

  /// <summary>
  /// Unpacks the run-length encoding, whose escape byte the file names for itself.
  /// </summary>
  /// <remarks>
  /// The escape introduces a count and then the value to repeat, and a count of zero means 256 —
  /// so the escape byte itself can only be written as a run of one, which costs three bytes. That
  /// is the price of letting the packer pick whichever byte the picture uses least.
  /// </remarks>
  private static byte[] _Unpack(ReadOnlySpan<byte> data) {
    var escape = data[2];
    var unpacked = new byte[SuperHiresFliFile.UnpackedSize];
    var at = 3;

    for (var target = 0; target < unpacked.Length;) {
      if (at >= data.Length)
        throw new InvalidDataException("A packed Super Hires FLI picture ends before its picture does.");

      var value = data[at++];
      var count = 1;

      if (value == escape) {
        if (at + 1 >= data.Length)
          throw new InvalidDataException("A Super Hires FLI run has no count or no value.");

        count = data[at++];
        if (count == 0)
          count = 256;

        value = data[at++];
      }

      while (count-- > 0 && target < unpacked.Length)
        unpacked[target++] = value;
    }

    return unpacked;
  }

  public static SuperHiresFliFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
