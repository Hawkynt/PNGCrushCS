using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace FileFormat.PostScript;

/// <summary>The <c>filter</c> operator: a file that decodes another one as it is read.</summary>
/// <remarks>
/// A filter decodes as it is read rather than all at once, which is not a detail. Several of these
/// encodings have no end marker of their own — hexadecimal data written straight into a program is
/// the common case — and the thing that ends them is the reader having read as many bytes as it
/// wanted. A filter that decoded eagerly would run on into the program that follows the data and
/// refuse the file over a slash.
/// <para/>
/// The two that cannot say where they end without being unpacked whole — deflate and LZW — take
/// everything left of what is under them. Both are used at the end of the data they wrap or over a
/// filter that does know its own extent, which is where a program can put them without the same
/// problem.
/// </remarks>
public static class PostScriptFilters {

  /// <summary>The most an all-at-once filter may decode to, which bounds what a wrong file can cost.</summary>
  public const int MaximumDecoded = 1 << 28;

  /// <summary>Makes a decoded file out of a source and a filter name.</summary>
  public static void Filter(PostScriptInterpreter interpreter) {
    ArgumentNullException.ThrowIfNull(interpreter);

    var nameValue = interpreter.Pop();
    var name = nameValue.Type == PsType.Name
      ? nameValue.Name
      : throw new PsErrorException("typecheck", $"A PostScript filter was named with {nameValue.TypeName}.");

    switch (name) {
      case "ASCIIHexDecode":
        _Push(interpreter, _Source(interpreter), static source => new(_Hex(source)));
        return;

      case "ASCII85Decode":
        _Push(interpreter, _Source(interpreter), static source => new(_Ascii85(source)));
        return;

      case "RunLengthDecode":
        _Push(interpreter, _Source(interpreter), static source => new(_RunLength(source)));
        return;

      case "SubFileDecode": {
        var marker = interpreter.PopString();
        var count = interpreter.PopInteger();
        _Push(interpreter, _Source(interpreter), source => new(_SubFile(source, count, marker)));
        return;
      }

      case "FlateDecode":
        _Push(interpreter, _Source(interpreter), static source => _Whole(_Flate(source)));
        return;

      case "LZWDecode":
        _Push(interpreter, _Source(interpreter), static source => _Whole(_Lzw(source)));
        return;

      case "NullEncode":
        _Push(interpreter, _Source(interpreter), static source => source);
        return;

      default:
        throw new PsUnsupportedException($"A PostScript program read its data through the filter {name}, which this reader does not decode.");
    }
  }

  private static void _Push(PostScriptInterpreter interpreter, PsFile source, Func<PsFile, PsFile> wrap)
    => interpreter.Push(PsObject.FromFile(wrap(source)));

  /// <summary>The file a filter reads from, under the options it does not take.</summary>
  private static PsFile _Source(PostScriptInterpreter interpreter) {
    // A filter dictionary carries options none of the filters here take; it is accepted so that a
    // program written the Level 2 way is read, and the options are the defaults either way.
    if (interpreter.Peek().Type == PsType.Dictionary)
      interpreter.Pop();

    var value = interpreter.Pop();
    return value.Type switch {
      PsType.File => value.File,
      PsType.String => new(value.String.Bytes, value.String.Offset, value.String.Offset + value.String.Length),
      _ => throw new PsErrorException("typecheck", $"A PostScript filter was told to read from {value.TypeName}.")
    };
  }

  private static PsFile _Whole(byte[] bytes) => new(bytes, 0, bytes.Length);

  /// <summary>
  /// Hexadecimal data, two characters a byte.
  /// </summary>
  /// <remarks>
  /// Whitespace between digits is ignored, a <c>&gt;</c> ends the data, and a digit left over at the
  /// end is completed with a zero — all three stated in the reference. Nothing else is allowed
  /// through: a character that is not a digit means the data is not what it says it is.
  /// </remarks>
  private static Func<int> _Hex(PsFile source) {
    var pending = -1;
    var done = false;

    return () => {
      if (done)
        return -1;

      for (;;) {
        var c = source.ReadByte();
        if (c < 0 || c == '>') {
          done = true;
          return pending >= 0 ? pending << 4 : -1;
        }

        if (PostScriptScanner.IsWhitespace(c))
          continue;

        var digit = c switch {
          >= '0' and <= '9' => c - '0',
          >= 'a' and <= 'f' => c - 'a' + 10,
          >= 'A' and <= 'F' => c - 'A' + 10,
          _ => throw new PsErrorException("ioerror", $"The character '{(char)c}' inside a hexadecimal stream in a PostScript program.")
        };

        if (pending < 0) {
          pending = digit;
          continue;
        }

        var value = (pending << 4) | digit;
        pending = -1;
        done = _Settle(source, '>');
        return value;
      }
    };
  }

  /// <summary>
  /// Takes the end marker off the source if the byte just handed over was the last one wanted.
  /// </summary>
  /// <remarks>
  /// A reader stops asking a filter for bytes when it has as many as it wants, which is one byte
  /// before the marker that ends the data. The program then carries on reading the file underneath,
  /// and finds that marker sitting where a token should be. Looking one character ahead after every
  /// byte and taking the marker when it is there leaves the file where the program expects it, and
  /// costs one peek.
  /// </remarks>
  private static bool _Settle(PsFile source, char marker) {
    for (;;) {
      var next = source.PeekByte();
      if (next < 0)
        return false;

      if (PostScriptScanner.IsWhitespace(next)) {
        source.ReadByte();
        continue;
      }

      if (next != marker)
        return false;

      source.ReadByte();
      return true;
    }
  }

  /// <summary>
  /// ASCII85 data: five characters to four bytes, <c>z</c> for four zeroes, ending at <c>~&gt;</c>.
  /// </summary>
  private static Func<int> _Ascii85(PsFile source) {
    var group = new int[5];
    var have = 0;
    var output = new byte[4];
    var ready = 0;
    var at = 0;
    var done = false;

    return () => {
      for (;;) {
        if (at < ready) {
          var value = output[at++];
          if (at == ready && _Settle(source, '~')) {
            if (source.PeekByte() == '>')
              source.ReadByte();

            done = true;
          }

          return value;
        }

        if (done)
          return -1;

        var c = source.ReadByte();
        if (c < 0 || c == '~') {
          if (c == '~' && source.PeekByte() == '>')
            source.ReadByte();

          done = true;
          if (have == 0)
            return -1;

          if (have == 1)
            throw new PsErrorException("ioerror", "An ASCII85 stream in a PostScript program ends on a single character, which encodes nothing.");

          for (var index = have; index < 5; ++index)
            group[index] = 84;

          ready = _Group(group, output, have - 1);
          at = 0;
          continue;
        }

        if (PostScriptScanner.IsWhitespace(c))
          continue;

        if (c == 'z' && have == 0) {
          output[0] = output[1] = output[2] = output[3] = 0;
          ready = 4;
          at = 0;
          continue;
        }

        if (c is < '!' or > 'u')
          throw new PsErrorException("ioerror", $"The character '{(char)c}' inside an ASCII85 stream in a PostScript program.");

        group[have++] = c - '!';
        if (have < 5)
          continue;

        ready = _Group(group, output, 4);
        at = 0;
        have = 0;
      }
    };
  }

  private static int _Group(int[] group, byte[] output, int count) {
    var value = 0L;
    for (var index = 0; index < 5; ++index)
      value = value * 85 + group[index];

    for (var index = 0; index < count; ++index)
      output[index] = (byte)(value >> (24 - index * 8));

    return count;
  }

  /// <summary>
  /// Run-length data: a length byte, then either that many literal bytes or one byte repeated.
  /// </summary>
  /// <remarks>
  /// A length under 128 means the next length plus one bytes are literal; over 128 means the next
  /// byte repeats 257 minus the length times; exactly 128 ends the data. The same scheme PackBits
  /// uses.
  /// </remarks>
  private static Func<int> _RunLength(PsFile source) {
    var repeat = 0;
    var repeated = 0;
    var literal = 0;
    var done = false;

    return () => {
      for (;;) {
        if (repeat > 0) {
          if (--repeat == 0)
            done = _EndOfRuns(source);

          return repeated;
        }

        if (literal > 0) {
          --literal;
          var value = source.ReadByte();
          if (value < 0) {
            done = true;
            return -1;
          }

          if (literal == 0)
            done = _EndOfRuns(source);

          return value;
        }

        if (done)
          return -1;

        var length = source.ReadByte();
        if (length < 0 || length == 128) {
          done = true;
          return -1;
        }

        if (length < 128) {
          literal = length + 1;
          continue;
        }

        repeated = source.ReadByte();
        if (repeated < 0) {
          done = true;
          return -1;
        }

        repeat = 257 - length;
      }
    };
  }

  /// <summary>Whether the byte after a completed run is the one that ends the data.</summary>
  private static bool _EndOfRuns(PsFile source) {
    if (source.PeekByte() != 128)
      return false;

    source.ReadByte();
    return true;
  }

  /// <summary>
  /// A stated number of bytes, or everything up to a marker.
  /// </summary>
  /// <remarks>
  /// A count of nought means the marker decides, which is how a program wraps a run of data it
  /// cannot count in advance. A count of nought with no marker means the whole of what is left,
  /// which is what the reference says an empty end-of-data string asks for.
  /// </remarks>
  private static Func<int> _SubFile(PsFile source, long count, PsString marker) {
    var left = count;
    var matched = 0;
    var replay = new Queue<byte>();
    var done = false;

    return () => {
      if (done)
        return -1;

      if (count > 0) {
        if (left-- <= 0) {
          done = true;
          return -1;
        }

        var value = source.ReadByte();
        if (value >= 0)
          return value;

        done = true;
        return -1;
      }

      for (;;) {
        if (replay.Count > 0)
          return replay.Dequeue();

        var value = source.ReadByte();
        if (value < 0) {
          done = true;
          return -1;
        }

        if (marker.Length == 0)
          return value;

        if (value == marker[matched]) {
          if (++matched < marker.Length)
            continue;

          done = true;
          return -1;
        }

        // A partial match that then failed was data after all, so it comes out before the byte that
        // broke it.
        for (var index = 0; index < matched; ++index)
          replay.Enqueue(marker[index]);

        matched = value == marker[0] ? 1 : 0;
        if (matched == 0)
          replay.Enqueue((byte)value);
      }
    };
  }

  private static byte[] _Flate(PsFile source) {
    using var input = new MemoryStream(source.Drain(), false);
    using var inflate = new ZLibStream(input, CompressionMode.Decompress);
    using var output = new MemoryStream();

    try {
      inflate.CopyTo(output);
    } catch (InvalidDataException failure) {
      throw new PsErrorException("ioerror", $"A deflated stream in a PostScript program could not be read: {failure.Message}");
    }

    _Check((int)output.Length);
    return output.ToArray();
  }

  /// <summary>
  /// The LZW variant PostScript and TIFF share: codes that grow from nine bits, with a clear code.
  /// </summary>
  /// <remarks>
  /// Code 256 clears the table and 257 ends the data. The table starts as the 256 single bytes and
  /// grows by one entry per code, the width going up one bit each time the table fills — one code
  /// early, which is the detail every implementation of this has to get the same way round.
  /// </remarks>
  private static byte[] _Lzw(PsFile source) {
    var output = new List<byte>();
    var table = new List<byte[]>();
    _LzwReset(table);

    var width = 9;
    var bits = 0;
    var buffer = 0;
    byte[]? previous = null;

    for (;;) {
      var value = source.ReadByte();
      if (value < 0)
        return output.ToArray();

      buffer = (buffer << 8) | value;
      bits += 8;

      while (bits >= width) {
        var code = (buffer >> (bits - width)) & ((1 << width) - 1);
        bits -= width;

        if (code == 257)
          return output.ToArray();

        if (code == 256) {
          _LzwReset(table);
          width = 9;
          previous = null;
          continue;
        }

        byte[] entry;
        if (code < table.Count && (code < 256 || table[code].Length > 0))
          entry = table[code];
        else if (previous != null)
          entry = [.. previous, previous[0]];
        else
          throw new PsErrorException("ioerror", "An LZW stream in a PostScript program begins with a code that names nothing.");

        output.AddRange(entry);
        _Check(output.Count);

        if (previous != null)
          table.Add([.. previous, entry[0]]);

        previous = entry;
        if (table.Count + 1 >= 1 << width && width < 12)
          ++width;
      }
    }
  }

  private static void _LzwReset(List<byte[]> table) {
    table.Clear();
    for (var index = 0; index < 256; ++index)
      table.Add([(byte)index]);

    table.Add([]);
    table.Add([]);
  }

  private static void _Check(int length) {
    if (length > MaximumDecoded)
      throw new PsErrorException("limitcheck", $"A filtered stream in a PostScript program decoded to more than {MaximumDecoded} bytes.");
  }
}
