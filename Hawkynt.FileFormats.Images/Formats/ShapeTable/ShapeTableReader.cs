using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.ShapeTable;

/// <summary>Reads shape tables from bytes, streams, or file paths.</summary>
public static class ShapeTableReader {

  /// <summary>The instruction that ends a shape.</summary>
  private const int _STOP = 8;

  public static ShapeTableFileType FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Shape table not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ShapeTableFileType FromStream(Stream stream) {
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

  public static ShapeTableFileType FromSpan(ReadOnlySpan<byte> data) {
    // The packed C64 forms identify themselves by a byte of their load header, so they are tried
    // first; the rest are told apart by length alone.
    if (data.Length >= 8 && _TryC64(data, out var c64))
      return c64;

    return data.Length switch {
      ShapeTableFileType.VectorFileSize => _ReadVectors(data),
      ShapeTableFileType.AtariFileSize => new() {
        Data = data.ToArray(),
        Kind = ShapeTableKind.AtariGraphics7,
        Width = 320,
        Height = 3844 / 40 * 2,
      },
      ShapeTableFileType.LoadstarFileSize => new() {
        Data = data.ToArray(),
        Kind = ShapeTableKind.Loadstar,
        Width = 160,
        Height = 200,
      },
      _ => throw new InvalidDataException($"Not a shape table: {data.Length} bytes."),
    };
  }

  /// <summary>
  /// Reads the packed C64 forms, whose third byte says both which program wrote them and where its
  /// escape byte and stream begin.
  /// </summary>
  private static bool _TryC64(ReadOnlySpan<byte> data, out ShapeTableFileType file) {
    file = default;

    var columns = 40;
    var height = 200;
    byte escape;
    int at;

    switch (data[2]) {
      case 0: escape = data[3]; at = 5; break;
      case 128: escape = data[3]; at = 4; break;

      case 167:
        if (data[3] != 25)
          return false;

        columns = 39;
        escape = data[4];
        at = 5;
        break;

      case 168:
        height = data[3] << 3;
        if (height == 0 || height > 200)
          return false;

        escape = data[4];
        at = 5;
        break;

      case 232:
        if (data[3] != 25)
          return false;

        escape = data[5];
        at = 6;
        break;

      default:
        return false;
    }

    var bitmapLength = height * columns;
    var unpacked = new byte[10001];
    var pending = new Run();

    try {
      _Unpack(data, ref at, escape, unpacked, 0, bitmapLength, ref pending);

      // The colour data uses zero as its escape, so a zero byte introduces a run rather than
      // standing for itself. Zero is the commonest value in a colour map, which is the point.
      _Unpack(data, ref at, 0, unpacked, bitmapLength, (bitmapLength >> 3) * 9, ref pending);

      switch (data[2]) {
        case 0:
          escape = 255;
          break;

        case 232:
          if (at >= data.Length || data[at++] != 255)
            return false;

          escape = 216;
          break;

        default:
          if (at != data.Length)
            return false;

          file = new() {
            Data = unpacked,
            Kind = ShapeTableKind.C64Hires,
            Width = columns << 3,
            Height = height,
            Columns = columns,
          };

          return true;
      }

      unpacked[10000] = data[4];
      _Unpack(data, ref at, escape, unpacked, 9000, 10000, ref pending);
      if (at != data.Length)
        return false;

      file = new() { Data = unpacked, Kind = ShapeTableKind.C64Multicolor, Width = 160, Height = 200 };

      return true;
    } catch (InvalidDataException) {
      return false;
    }
  }

  /// <summary>A run left part-written when a section filled up, to be finished by the next one.</summary>
  /// <remarks>
  /// The three sections of a packed shape table are one stream read three times with a different
  /// escape byte each time, not three streams. A run that reaches the end of the bitmap therefore
  /// carries on into the colour map with the value it already had, and only what follows it is read
  /// under the new escape. Restarting at each boundary shifts everything after such a run.
  /// </remarks>
  private struct Run {
    public int Count;
    public byte Value;
  }

  /// <summary>Unpacks one section, whose escape byte introduces a count and a value.</summary>
  private static void _Unpack(
    ReadOnlySpan<byte> data, ref int at, byte escape, Span<byte> target, int from, int to, ref Run pending) {
    for (var i = from; i < to;) {
      if (pending.Count == 0) {
        if (at >= data.Length)
          throw new InvalidDataException("A shape table's stream ends before its picture does.");

        var value = data[at++];
        var count = 1;

        if (value == escape) {
          if (at + 1 >= data.Length)
            throw new InvalidDataException("A run has no count or no value.");

          count = data[at++];
          if (count == 0)
            count = 256;

          value = data[at++];
        }

        pending = new() { Count = count, Value = value };
      }

      while (pending.Count > 0 && i < to) {
        target[i++] = pending.Value;
        --pending.Count;
      }
    }
  }

  /// <summary>
  /// Reads Blazing Paddles' shapes and works out where each one goes.
  /// </summary>
  /// <remarks>
  /// A shape is a run of drawing instructions, not pixels, so it has no size until it has been
  /// walked. The shapes are then laid out in rows across 160 pixels, and because each has its own
  /// extent above and below its starting point, a row's height is only known once every shape in it
  /// has been measured — which is why the placement is done in two passes.
  /// </remarks>
  private static ShapeTableFileType _ReadVectors(ReadOnlySpan<byte> data) {
    var xs = new List<int>();
    var ys = new List<int>();
    int x = 0, y = 0, lineStart = 0, lineTop = 0, lineBottom = 0, width = 0, count = 0;

    for (var i = 0; i < 256; ++i) {
      if (!_TryMeasure(data, i, out var box))
        break;

      var shapeWidth = box.Right - box.Left + 2;
      if (x + shapeWidth > 160) {
        y -= lineTop;
        while (lineStart < i)
          ys[lineStart++] = y;

        width = Math.Max(width, x);
        x = 0;
        y += lineBottom + 2;
        lineTop = box.Top;
        lineBottom = box.Bottom;
      }

      xs.Add(x - box.Left);
      ys.Add(0);
      x += shapeWidth;
      lineTop = Math.Min(lineTop, box.Top);
      lineBottom = Math.Max(lineBottom, box.Bottom);
      count = i + 1;
    }

    y -= lineTop;
    while (lineStart < count)
      ys[lineStart++] = y;

    width = Math.Max(width, x);
    y += lineBottom + 1;

    if (count == 0 || width > 160 || y > 240)
      throw new InvalidDataException("A Blazing Paddles shape table lays out to no picture.");

    var placements = new (int X, int Y)[count];
    for (var i = 0; i < count; ++i)
      placements[i] = (xs[i], ys[i]);

    return new() {
      Data = data.ToArray(),
      Kind = ShapeTableKind.Vectors,
      Width = width << 1,
      Height = y,
      Placements = placements,
    };
  }

  /// <summary>Walks a shape without drawing it, to find how far it reaches in each direction.</summary>
  private static bool _TryMeasure(ReadOnlySpan<byte> data, int index, out (int Left, int Top, int Right, int Bottom) box) {
    box = default;

    if (!_TryStart(data, index, out var at))
      return false;

    int x = 0, y = 0;
    while (at < data.Length) {
      var control = data[at++];
      if (control == _STOP)
        return true;

      // The top nibble is a repeat count and the bottom two bits a direction.
      var length = (control >> 4) + 1;
      switch (control & 3) {
        case 0: x += length; box.Right = Math.Max(box.Right, x); break;
        case 1: x -= length; box.Left = Math.Min(box.Left, x); break;
        case 2: y -= length; box.Top = Math.Min(box.Top, y); break;
        default: y += length; box.Bottom = Math.Max(box.Bottom, y); break;
      }
    }

    return false;
  }

  /// <summary>Draws one shape, a step at a time, with the pen down unless the instruction lifts it.</summary>
  internal static void DrawVector(ReadOnlySpan<byte> data, Span<byte> frame, int offset, int index, int width) {
    if (!_TryStart(data, index, out var at))
      return;

    while (at < data.Length) {
      int control = data[at++];
      if (control == _STOP)
        return;

      for (; control >= 0; control -= 16) {
        if ((control & 4) == 0 && offset >= 0 && offset + 1 < frame.Length)
          frame[offset + 1] = frame[offset] = 14;

        offset += (control & 3) switch {
          0 => 2,
          1 => -2,
          2 => -width,
          _ => width,
        };
      }
    }
  }

  /// <summary>Where a shape's instructions begin, from the table of addresses at the head.</summary>
  private static bool _TryStart(ReadOnlySpan<byte> data, int index, out int offset) {
    offset = 0;
    if (index * 2 + 1 >= data.Length)
      return false;

    offset = data[index * 2] + (data[index * 2 + 1] << 8) - ShapeTableFileType.VectorLoadAddress;

    return offset >= 0;
  }

  public static ShapeTableFileType FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
