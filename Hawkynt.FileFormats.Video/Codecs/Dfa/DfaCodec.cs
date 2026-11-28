using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.Dfa;

/// <summary>The chunk types a DFA frame is made of, in FFmpeg's numbering.</summary>
internal static class DfaChunk {
  internal const int Palette = 1;
  internal const int Copy = 2;
  internal const int Tsw1 = 3;
  internal const int Bdlt = 4;
  internal const int Wdlt = 5;
  internal const int Tdlt = 6;
  internal const int Dsw1 = 7;
  internal const int Blck = 8;
  internal const int Dds1 = 9;

  /// <summary>The interleaved layout of the version that stores four quarter-width columns of lines.</summary>
  internal const int InterleavedVersion = 0x100;

  internal static readonly string[] Names = ["COPY", "TSW1", "BDLT", "WDLT", "TDLT", "DSW1", "BLCK", "DDS1"];
}

/// <summary>
/// A cursor over a packet that reads as FFmpeg's <c>GetByteContext</c> does: a read past the end
/// yields zero and leaves the cursor at the end, rather than failing.
/// </summary>
internal ref struct DfaReader(ReadOnlySpan<byte> data) {

  private readonly ReadOnlySpan<byte> _data = data;
  private int _at;

  public readonly int Left => this._data.Length - this._at;

  public void Skip(int count) => this._at += Math.Min(this.Left, count);

  public int Byte() {
    if (this.Left < 1)
      return 0;
    return this._data[this._at++];
  }

  public readonly int PeekByte() => this.Left < 1 ? 0 : this._data[this._at];

  public int UInt16() {
    if (this.Left < 2) {
      this._at = this._data.Length;
      return 0;
    }
    var value = BinaryPrimitives.ReadUInt16LittleEndian(this._data[this._at..]);
    this._at += 2;
    return value;
  }

  public uint UInt32() {
    if (this.Left < 4) {
      this._at = this._data.Length;
      return 0;
    }
    var value = BinaryPrimitives.ReadUInt32LittleEndian(this._data[this._at..]);
    this._at += 4;
    return value;
  }

  public uint UInt24BigEndian() {
    if (this.Left < 3) {
      this._at = this._data.Length;
      return 0;
    }
    var value = (uint)(this._data[this._at] << 16 | this._data[this._at + 1] << 8 | this._data[this._at + 2]);
    this._at += 3;
    return value;
  }

  /// <summary>Copies as many of <paramref name="count"/> bytes as remain; how many that was.</summary>
  public int Buffer(Span<byte> destination, int count) {
    var copied = Math.Min(this.Left, count);
    this._data.Slice(this._at, copied).CopyTo(destination);
    this._at += copied;
    return copied;
  }
}

/// <summary>
/// The DFA picture decoder, FFmpeg's <c>libavcodec/dfa.c</c> (LGPL-2.1-or-later): a persistent
/// eight-bit buffer that each frame's chunks rewrite, and a 6-bit palette.
/// </summary>
/// <remarks>
/// One departure, deliberate: FFmpeg's DDS1 skip tests <c>frame - frame_end &lt; v</c>, which can
/// never be false for a skip inside the picture, so FFmpeg refuses every DDS1 chunk ever written —
/// and a DDS1 picture cannot be drawn without skips, because its doubled pixels leave every other
/// line to be stepped over. This decoder tests what was plainly meant, <c>frame_end - frame &lt; v</c>.
/// Everything else, the failure paths included, is FFmpeg's: a chunk that fails leaves the buffer as
/// far as it got, and the next frame draws over that.
/// </remarks>
internal sealed class DfaFrameDecoder(int width, int height, int version) {

  private readonly byte[] _buffer = new byte[width * height];

  /// <summary>The palette as FFmpeg holds it, 0xAARRGGBB.</summary>
  public uint[] Palette { get; } = new uint[256];

  public int Width => width;
  public int Height => height;

  /// <summary>Applies one frame's chunks; false and the chunk's name when one is damaged.</summary>
  public bool Decode(ReadOnlySpan<byte> packet, out string? damaged) {
    damaged = null;
    var reader = new DfaReader(packet);
    while (reader.Left > 0) {
      if (reader.Left < 12) {
        damaged = "chunk header";
        return false;
      }
      reader.Skip(4);
      var size = reader.UInt32();
      var type = reader.UInt32();
      if (type == 0)
        break;
      if (type == DfaChunk.Palette) {
        var count = (int)Math.Min(size / 3, 256);
        for (var i = 0; i < count; ++i) {
          var colour = reader.UInt24BigEndian() << 2;
          this.Palette[i] = colour | 0xFF000000u | ((colour >> 6) & 0x30303);
        }
      } else if (type <= 9) {
        var ok = type switch {
          DfaChunk.Copy => this._Copy(ref reader),
          DfaChunk.Tsw1 => this._Tsw1(ref reader),
          DfaChunk.Bdlt => this._Bdlt(ref reader),
          DfaChunk.Wdlt => this._Wdlt(ref reader),
          DfaChunk.Tdlt => this._Tdlt(ref reader),
          DfaChunk.Dsw1 => this._Dsw1(ref reader),
          DfaChunk.Blck => this._Blck(),
          _ => this._Dds1(ref reader),
        };
        if (!ok) {
          damaged = DfaChunk.Names[type - 2];
          return false;
        }
      }
    }
    return true;
  }

  /// <summary>The picture in display order: the buffer itself, or for version 0x100 its four interleaved columns.</summary>
  public byte[] Picture() {
    if (version != DfaChunk.InterleavedVersion)
      return (byte[])this._buffer.Clone();
    var picture = new byte[width * height];
    var stride = height / 4 * width;
    for (var y = 0; y < height; ++y) {
      var line = (y & 3) * (width / 4) + y / 4 * width;
      for (var x = 0; x < width; ++x)
        picture[y * width + x] = this._buffer[line + x / 4 + (x & 3) * stride];
    }
    return picture;
  }

  /// <summary>Copies <paramref name="count"/> bytes from <paramref name="back"/> behind, forward, so a short distance repeats; distance zero copies nothing.</summary>
  private static void _CopyBack(byte[] buffer, int at, int back, int count) {
    if (back == 0)
      return;
    for (var i = 0; i < count; ++i)
      buffer[at + i] = buffer[at + i - back];
  }

  private bool _Copy(ref DfaReader reader) {
    var size = width * height;
    return reader.Buffer(this._buffer, size) == size;
  }

  private bool _Tsw1(ref DfaReader reader) {
    var end = width * height;
    int mask = 0x10000, bits = 0;
    var segments = reader.UInt32();
    var offset = reader.UInt32();
    if (segments == 0 && offset == end)
      return true; // the frame is unchanged
    if (end <= offset)
      return false;
    var at = (int)offset;
    while (segments-- > 0) {
      if (reader.Left < 2)
        return false;
      if (mask == 0x10000) {
        bits = reader.UInt16();
        mask = 1;
      }
      if (end - at < 2)
        return false;
      if ((bits & mask) != 0) {
        var v = reader.UInt16();
        var back = (v & 0x1FFF) << 1;
        var count = ((v >> 13) + 2) << 1;
        if (at < back || end - at < count)
          return false;
        _CopyBack(this._buffer, at, back, count);
        at += count;
      } else {
        this._buffer[at++] = (byte)reader.Byte();
        this._buffer[at++] = (byte)reader.Byte();
      }
      mask <<= 1;
    }
    return true;
  }

  private bool _Dsw1(ref DfaReader reader) {
    var end = width * height;
    int mask = 0x10000, bits = 0;
    var segments = reader.UInt16();
    var at = 0;
    while (segments-- > 0) {
      if (reader.Left < 2)
        return false;
      if (mask == 0x10000) {
        bits = reader.UInt16();
        mask = 1;
      }
      if (end - at < 2)
        return false;
      if ((bits & mask) != 0) {
        var v = reader.UInt16();
        var back = (v & 0x1FFF) << 1;
        var count = ((v >> 13) + 2) << 1;
        if (at < back || end - at < count)
          return false;
        _CopyBack(this._buffer, at, back, count);
        at += count;
      } else if ((bits & (mask << 1)) != 0) {
        at += reader.UInt16();
      } else {
        this._buffer[at++] = (byte)reader.Byte();
        this._buffer[at++] = (byte)reader.Byte();
      }
      mask <<= 2;
    }
    return true;
  }

  private bool _Dds1(ref DfaReader reader) {
    var end = width * height;
    int mask = 0x10000, bits = 0;
    if (((width | height) & 1) != 0)
      return false;
    var segments = reader.UInt16();
    var at = 0;
    var b = this._buffer;
    while (segments-- > 0) {
      if (reader.Left < 2)
        return false;
      if (mask == 0x10000) {
        bits = reader.UInt16();
        mask = 1;
      }
      if ((bits & mask) != 0) {
        var v = reader.UInt16();
        var back = (v & 0x1FFF) << 2;
        var count = ((v >> 13) + 2) << 1;
        if (at < back || end - at < count * 2 + width)
          return false;
        for (var i = 0; i < count; ++i) {
          b[at] = b[at + 1] = b[at + width] = b[at + width + 1] = b[at - back];
          at += 2;
        }
      } else if ((bits & (mask << 1)) != 0) {
        var skip = reader.UInt16() * 2;
        if (end - at < skip) // FFmpeg writes this the other way round, and so refuses every skip
          return false;
        at += skip;
      } else {
        if (width < 4 || end - at < width + 4)
          return false;
        b[at] = b[at + 1] = b[at + width] = b[at + width + 1] = (byte)reader.Byte();
        at += 2;
        b[at] = b[at + 1] = b[at + width] = b[at + width + 1] = (byte)reader.Byte();
        at += 2;
      }
      mask <<= 2;
    }
    return true;
  }

  private bool _Bdlt(ref DfaReader reader) {
    var count = reader.UInt16();
    if (count >= height)
      return false;
    var frame = width * count;
    var lines = reader.UInt16();
    if (count + lines > height)
      return false;
    while (lines-- > 0) {
      if (reader.Left < 1)
        return false;
      var line = frame;
      frame += width;
      var segments = reader.Byte();
      while (segments-- > 0) {
        if (frame - line <= reader.PeekByte())
          return false;
        line += reader.Byte();
        var run = (sbyte)reader.Byte();
        if (run >= 0) {
          if (frame - line < run)
            return false;
          if (reader.Buffer(this._buffer.AsSpan(line), run) != run)
            return false;
          line += run;
        } else {
          var length = -run;
          if (frame - line < length)
            return false;
          this._buffer.AsSpan(line, length).Fill((byte)reader.Byte());
          line += length;
        }
      }
    }
    return true;
  }

  private bool _Wdlt(ref DfaReader reader) {
    var end = width * height;
    var frame = 0;
    var y = 0;
    var lines = reader.UInt16();
    if (lines > height)
      return false;
    while (lines-- > 0) {
      if (reader.Left < 2)
        return false;
      var segments = reader.UInt16();
      while ((segments & 0xC000) == 0xC000) {
        var skipLines = (uint)-(short)segments;
        var delta = -(short)segments * (long)width;
        if (end - frame <= delta || y + lines + skipLines > height)
          return false;
        frame += (int)delta;
        y += (int)skipLines;
        segments = reader.UInt16();
      }
      if (end <= frame || end - frame < width)
        return false;
      if ((segments & 0x8000) != 0) {
        this._buffer[frame + width - 1] = (byte)segments;
        segments = reader.UInt16();
      }
      var line = frame;
      frame += width;
      ++y;
      while (segments-- > 0) {
        if (frame - line <= reader.PeekByte())
          return false;
        line += reader.Byte();
        var run = (sbyte)reader.Byte();
        if (run >= 0) {
          if (frame - line < run * 2)
            return false;
          if (reader.Buffer(this._buffer.AsSpan(line), run * 2) != run * 2)
            return false;
          line += run * 2;
        } else {
          var length = -run;
          if (frame - line < length * 2)
            return false;
          var v = reader.UInt16();
          for (var i = 0; i < length; ++i) {
            this._buffer[line++] = (byte)v;
            this._buffer[line++] = (byte)(v >> 8);
          }
        }
      }
    }
    return true;
  }

  private bool _Tdlt(ref DfaReader reader) {
    var end = width * height;
    var frame = 0;
    var segments = reader.UInt32();
    while (segments-- > 0) {
      if (reader.Left < 2)
        return false;
      var copy = reader.Byte() * 2;
      var skip = reader.Byte() * 2;
      if (end - frame < copy + skip || reader.Left < copy)
        return false;
      frame += skip;
      reader.Buffer(this._buffer.AsSpan(frame), copy);
      frame += copy;
    }
    return true;
  }

  private bool _Blck() {
    Array.Clear(this._buffer);
    return true;
  }
}

/// <summary>
/// Writes DFA frames: a <c>PAL1</c> chunk when the palette changes, then the smallest of a raw
/// <c>COPY</c>, a <c>BLCK</c> clear, a <c>TSW1</c> LZ77 picture and a <c>DSW1</c> delta that skips what has
/// not changed, and the <c>EOFR</c> that ends every frame.
/// </summary>
internal sealed class DfaEncoder(int width, int height) {

  private const int _WINDOW = 0x1FFF << 1;
  private const int _LONGEST = 18;
  private const int _CHAIN = 48;

  private readonly byte[] _previous = new byte[width * height];
  private readonly byte[] _palette = new byte[768];
  private bool _started;

  /// <summary>Expands a 6-bit component to eight bits as the decoder does.</summary>
  internal static int Expand(int component) => (component << 2) | (component >> 4);

  /// <summary>The 6-bit component whose expansion lies nearest the 8-bit one.</summary>
  internal static int Quantise(int component) {
    var best = component >> 2;
    for (var candidate = Math.Max(0, (component >> 2) - 1); candidate <= Math.Min(63, (component >> 2) + 1); ++candidate)
      if (Math.Abs(Expand(candidate) - component) < Math.Abs(Expand(best) - component))
        best = candidate;
    return best;
  }

  /// <summary>One frame's packet; <paramref name="key"/> writes the whole picture without reference to the last.</summary>
  public byte[] Encode(ReadOnlySpan<byte> picture, ReadOnlySpan<byte> palette, bool key) {
    using var output = new MemoryStream();
    Span<byte> sixBit = stackalloc byte[768];
    for (var i = 0; i < 768; ++i)
      sixBit[i] = (byte)Quantise(i < palette.Length ? palette[i] : 0);
    if (!this._started || !sixBit.SequenceEqual(this._palette)) {
      _Chunk(output, "PAL1", DfaChunk.Palette, sixBit);
      sixBit.CopyTo(this._palette);
    }

    var candidates = new List<(string Name, int Type, byte[] Body)> { ("COPY", DfaChunk.Copy, picture.ToArray()) };
    if (!picture.ContainsAnyExcept((byte)0))
      candidates.Add(("BLCK", DfaChunk.Blck, []));
    if (_Tsw1(picture, key ? 0 : _FirstChange(picture, this._previous), picture.Length) is { } tsw1)
      candidates.Add(("TSW1", DfaChunk.Tsw1, tsw1));
    if (!key && this._started && _Dsw1(picture, this._previous) is { } dsw1)
      candidates.Add(("DSW1", DfaChunk.Dsw1, dsw1));

    var best = candidates[0];
    foreach (var candidate in candidates)
      if (candidate.Body.Length < best.Body.Length)
        best = candidate;
    _Chunk(output, best.Name, best.Type, best.Body);
    _Chunk(output, "EOFR", 0, []);

    picture.CopyTo(this._previous);
    this._started = true;
    return output.ToArray();
  }

  private static void _Chunk(Stream output, string name, int type, ReadOnlySpan<byte> body) {
    Span<byte> header = stackalloc byte[12];
    for (var i = 0; i < 4; ++i)
      header[i] = (byte)name[i];
    BinaryPrimitives.WriteUInt32LittleEndian(header[4..], (uint)body.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(header[8..], (uint)type);
    output.Write(header);
    output.Write(body);
  }

  private static int _FirstChange(ReadOnlySpan<byte> picture, ReadOnlySpan<byte> previous) => picture.CommonPrefixLength(previous);

  /// <summary>The longest back reference at an even distance that a TSW1 or DSW1 copy can express.</summary>
  private sealed class Matcher(byte[] data) {
    private readonly int[] _head = _NewHead();
    private readonly int[] _next = new int[data.Length];

    private static int[] _NewHead() {
      var head = new int[1 << 16];
      Array.Fill(head, -1);
      return head;
    }

    private int _Hash(int at) => ((data[at] << 8 | data[at + 1]) * 31 + (data[at + 2] << 8 | data[at + 3])) & 0xFFFF;

    /// <summary>Records every position before <paramref name="limit"/> not yet recorded.</summary>
    public void Insert(int from, int limit) {
      for (var at = from; at < limit; ++at) {
        if (at + 4 > data.Length)
          return;
        var hash = this._Hash(at);
        this._next[at] = this._head[hash];
        this._head[hash] = at;
      }
    }

    public (int Back, int Count) Find(int at) {
      if (at + 4 > data.Length)
        return (0, 0);
      var available = Math.Min(_LONGEST, data.Length - at) & ~1;
      int bestBack = 0, bestCount = 0, chain = _CHAIN;
      for (var candidate = this._head[this._Hash(at)]; candidate >= 0 && chain-- > 0; candidate = this._next[candidate]) {
        var back = at - candidate;
        if (back > _WINDOW)
          break;
        if ((back & 1) != 0 || back == 0)
          continue;
        var count = 0;
        while (count < available && data[candidate + count] == data[at + count])
          ++count;
        count &= ~1;
        if (count > bestCount) {
          (bestBack, bestCount) = (back, count);
          if (count == available)
            break;
        }
      }
      return bestCount >= 4 ? (bestBack, bestCount) : (0, 0);
    }
  }

  /// <summary>A TSW1 body writing from <paramref name="start"/> to <paramref name="end"/>; null where the picture cannot be expressed so.</summary>
  private static byte[]? _Tsw1(ReadOnlySpan<byte> picture, int start, int end) {
    var size = picture.Length;
    if (start >= size)
      return _Body(0, (uint)size, []); // unchanged
    // Pairs run from the start to the picture's end; an odd remainder cannot be written.
    if (((end - start) & 1) != 0)
      return null;
    var data = picture.ToArray();
    var matcher = new Matcher(data);
    matcher.Insert(0, start);
    var ops = new List<(bool Copy, int A, int B)>();
    for (var at = start; at < end;) {
      var (back, count) = matcher.Find(at);
      if (count >= 4) {
        ops.Add((true, back, count));
        matcher.Insert(at, at + count);
        at += count;
      } else {
        ops.Add((false, data[at], data[at + 1]));
        matcher.Insert(at, at + 2);
        at += 2;
      }
    }
    return _Body((uint)ops.Count, (uint)start, _Pack(ops));
  }

  private static byte[] _Body(uint segments, uint offset, byte[] ops) {
    var body = new byte[8 + ops.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(body, segments);
    BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(4), offset);
    ops.CopyTo(body, 8);
    return body;
  }

  /// <summary>A DSW1 body: unchanged runs skipped, the rest copied back or written in pairs; null where it will not fit.</summary>
  private static byte[]? _Dsw1(ReadOnlySpan<byte> picture, ReadOnlySpan<byte> previous) {
    var data = picture.ToArray();
    var size = data.Length;
    var matcher = new Matcher(data);
    var ops = new List<(bool Copy, int A, int B)>();
    var at = 0;
    var recorded = 0;
    while (true) {
      var unchanged = data.AsSpan(at).CommonPrefixLength(previous[at..]);
      if (at + unchanged >= size)
        break;
      if (unchanged >= 2) {
        // A skip is two bytes and two flag bits, the same as one pair, so only a run longer than a pair pays.
        var step = Math.Min(unchanged, 65535);
        ops.Add((false, step, -1));
        at += step;
        continue;
      }
      if (size - at < 2)
        return null;
      matcher.Insert(recorded, at);
      recorded = Math.Max(recorded, at);
      var (back, count) = matcher.Find(at);
      if (count >= 4) {
        ops.Add((true, back, count));
        at += count;
      } else {
        ops.Add((false, data[at], data[at + 1]));
        at += 2;
      }
    }
    if (ops.Count > 65535)
      return null;

    using var output = new MemoryStream();
    Span<byte> word = stackalloc byte[2];
    BinaryPrimitives.WriteUInt16LittleEndian(word, (ushort)ops.Count);
    output.Write(word);
    for (var group = 0; group < ops.Count; group += 8) {
      var flags = 0;
      for (var i = 0; i < 8 && group + i < ops.Count; ++i) {
        var op = ops[group + i];
        if (op.Copy)
          flags |= 1 << (2 * i);
        else if (op.B < 0)
          flags |= 2 << (2 * i);
      }
      BinaryPrimitives.WriteUInt16LittleEndian(word, (ushort)flags);
      output.Write(word);
      for (var i = 0; i < 8 && group + i < ops.Count; ++i)
        _WriteOp(output, ops[group + i]);
    }
    return output.ToArray();
  }

  /// <summary>TSW1's operations behind a flag word for every sixteen, one bit each.</summary>
  private static byte[] _Pack(List<(bool Copy, int A, int B)> ops) {
    using var output = new MemoryStream();
    Span<byte> word = stackalloc byte[2];
    const int perWord = 16;
    for (var group = 0; group < ops.Count; group += perWord) {
      var flags = 0;
      for (var i = 0; i < perWord && group + i < ops.Count; ++i)
        if (ops[group + i].Copy)
          flags |= 1 << i;
      BinaryPrimitives.WriteUInt16LittleEndian(word, (ushort)flags);
      output.Write(word);
      for (var i = 0; i < perWord && group + i < ops.Count; ++i)
        _WriteOp(output, ops[group + i]);
    }
    return output.ToArray();
  }

  private static void _WriteOp(Stream output, (bool Copy, int A, int B) op) {
    Span<byte> word = stackalloc byte[2];
    if (op.Copy)
      BinaryPrimitives.WriteUInt16LittleEndian(word, (ushort)(((op.B / 2 - 2) << 13) | (op.A >> 1)));
    else if (op.B < 0)
      BinaryPrimitives.WriteUInt16LittleEndian(word, (ushort)op.A);
    else {
      word[0] = (byte)op.A;
      word[1] = (byte)op.B;
    }
    output.Write(word);
  }
}
