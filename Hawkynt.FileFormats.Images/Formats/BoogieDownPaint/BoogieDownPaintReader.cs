using System;
using System.IO;
using System.Text;

namespace FileFormat.BoogieDownPaint;

/// <summary>Reads Boogie Down Paint pictures from bytes, streams, or file paths.</summary>
public static class BoogieDownPaintReader {

  /// <summary>The bytes the earliest form's loader begins with, which is all it has for a header.</summary>
  private static ReadOnlySpan<byte> _LoaderSignature => [2, 4, 16, 54, 48, 48];

  public static BoogieDownPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static BoogieDownPaintFile FromStream(Stream stream) {
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

  public static BoogieDownPaintFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 13)
      throw new InvalidDataException($"Not a Boogie Down Paint picture: {data.Length} bytes.");

    // One form carries a version string, one a recognisable loader, and the oldest neither.
    if (data.Slice(2, _LoaderSignature.Length).SequenceEqual(_LoaderSignature))
      return new() { ScreenData = _UnpackEscaped(data, 10, data[8], -1) };

    if (Encoding.ASCII.GetString(data.Slice(2, 8)) == "BDP 5.00")
      return new() { ScreenData = _UnpackEscaped(data, 12, data[10], data[11]) };

    return new() { ScreenData = _UnpackCommanded(data) };
  }

  /// <summary>
  /// Unpacks the two forms whose escape bytes the file names for itself.
  /// </summary>
  /// <remarks>
  /// The later form names two: one introducing a count of a single byte and one a count of two, so
  /// a long run costs a byte more only when it is long. The earlier names one, whose count of zero
  /// stands for 256. Everything that is not an escape stands for itself.
  /// </remarks>
  private static byte[] _UnpackEscaped(ReadOnlySpan<byte> data, int offset, byte shortEscape, int longEscape) {
    var unpacked = new byte[BoogieDownPaintFile.UnpackedSize];
    var at = offset;

    for (var target = 0; target < unpacked.Length;) {
      if (at >= data.Length)
        throw new InvalidDataException("A Boogie Down Paint picture ends before its picture does.");

      var b = data[at++];
      var count = 1;

      if (b == shortEscape || (longEscape >= 0 && b == longEscape)) {
        if (at >= data.Length)
          throw new InvalidDataException("A run has no count.");

        count = data[at++];
        if (longEscape >= 0 && b == longEscape) {
          if (at >= data.Length)
            throw new InvalidDataException("A long run has no second count byte.");

          count |= data[at++] << 8;
        } else if (count == 0)
          count = 256;

        if (at >= data.Length)
          throw new InvalidDataException("A run has no value.");

        b = data[at++];
      }

      while (count-- > 0 && target < unpacked.Length)
        unpacked[target++] = b;
    }

    return unpacked;
  }

  /// <summary>
  /// Unpacks the oldest form, which has two fixed commands and nothing else.
  /// </summary>
  /// <remarks>
  /// Every byte of the stream is a command: one repeats a value, the other introduces a run of
  /// literals with a two-byte count. Nothing stands for itself, which is why the format needs no
  /// escape and no header — but also why any byte that is neither command makes the file invalid.
  /// </remarks>
  private static byte[] _UnpackCommanded(ReadOnlySpan<byte> data) {
    var unpacked = new byte[BoogieDownPaintFile.UnpackedSize];
    var at = 2;

    for (var target = 0; target < unpacked.Length;) {
      if (at >= data.Length)
        throw new InvalidDataException("A Boogie Down Paint picture ends before its picture does.");

      switch (data[at++]) {
        case 255: {
          if (at + 1 >= data.Length)
            throw new InvalidDataException("A repeated run has no count or no value.");

          int count = data[at++];
          if (count == 0)
            count = 256;

          var value = data[at++];
          while (count-- > 0 && target < unpacked.Length)
            unpacked[target++] = value;

          break;
        }

        case 254: {
          if (at + 1 >= data.Length)
            throw new InvalidDataException("A run of literals has no count.");

          var count = data[at] | (data[at + 1] << 8);
          at += 2;

          while (count-- > 0 && target < unpacked.Length) {
            if (at >= data.Length)
              throw new InvalidDataException("A run of literals runs past the end of the file.");

            unpacked[target++] = data[at++];
          }

          break;
        }

        default:
          throw new InvalidDataException("Not a Boogie Down Paint picture: a byte that is no command.");
      }
    }

    return unpacked;
  }

  public static BoogieDownPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
