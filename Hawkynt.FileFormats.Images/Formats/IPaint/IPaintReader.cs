using System;
using System.IO;

namespace FileFormat.IPaint;

/// <summary>Reads I Paint pictures from bytes, streams, or file paths.</summary>
public static class IPaintReader {

  /// <summary>Where the packed bitmap starts.</summary>
  private const int _BITMAP_OFFSET = 18;

  public static IPaintFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static IPaintFile FromStream(Stream stream) {
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

  public static IPaintFile FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 20 || data[2] != 'B' || data[3] != 'R' || data[4] != 'U' || data[5] != 'S'
        || data[6] != 4 || data[10] != 1 || data[11] != 2)
      throw new InvalidDataException("Not an I Paint picture.");

    int columns = data[12];
    if (columns is 0 or > 90)
      throw new InvalidDataException($"A picture {columns} cells across is not one I Paint wrote.");

    var height = data[13] | (data[14] << 8);
    if (height is 0 or > 700)
      throw new InvalidDataException($"A picture {height} rows tall is not one I Paint wrote.");

    var at = _BITMAP_OFFSET;
    var pending = new _Run();
    var bitmap = new byte[height * columns];
    _Unpack(data, ref at, bitmap, 0, bitmap.Length, ref pending);

    // Colour is optional and announced by a tag; without it the picture is black on white.
    if (at + 4 >= data.Length
        || data[at] != 'C' || data[at + 1] != 'O' || data[at + 2] != 'L' || data[at + 3] != 'R')
      return new() { Columns = columns, Height = height, Bitmap = bitmap, Colors = [] };

    at += 4;
    var blocks = (height + 7) >> 3;
    var colors = new byte[blocks * columns * 2];

    // One block of colour per eight rows, each block two rows of cells.
    for (var block = 0; block < blocks; ++block)
      _Unpack(data, ref at, colors, block * columns * 2, columns * 2, ref pending);

    return new() { Columns = columns, Height = height, Bitmap = bitmap, Colors = colors };
  }

  /// <summary>A run left part-written when a section filled up, to be finished by the next one.</summary>
  /// <remarks>
  /// The bitmap and every block of colour are one stream, read a section at a time, and the tag
  /// between them moves the read position without ending the run in progress. A run that reaches
  /// the end of the bitmap therefore carries on into the first block of colour.
  /// </remarks>
  private struct _Run {
    public int Count;
    public int Value;
  }

  /// <summary>
  /// Unpacks a section of a run-length stream whose command byte's top bit says whether what
  /// follows is one value to repeat or that many bytes to take as they are.
  /// </summary>
  private static void _Unpack(
    ReadOnlySpan<byte> data, ref int at, Span<byte> target, int from, int count, ref _Run pending) {
    for (var i = from; i < from + count;) {
      while (pending.Count == 0) {
        if (at >= data.Length)
          throw new InvalidDataException("An I Paint stream ends before its picture does.");

        var command = data[at++];
        pending.Count = command & 127;

        // Literals have no value of their own; each byte of the run is read as it is reached.
        if (command < 128) {
          pending.Value = -1;
          continue;
        }

        if (at >= data.Length)
          throw new InvalidDataException("An I Paint run has no value.");

        pending.Value = data[at++];
      }

      --pending.Count;
      if (pending.Value >= 0) {
        target[i++] = (byte)pending.Value;
        continue;
      }

      if (at >= data.Length)
        throw new InvalidDataException("An I Paint stream ends inside a run of literals.");

      target[i++] = data[at++];
    }
  }

  public static IPaintFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
