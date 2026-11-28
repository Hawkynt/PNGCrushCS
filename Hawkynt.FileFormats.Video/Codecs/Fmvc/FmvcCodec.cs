using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.Fmvc;

/// <summary>
/// A read cursor that behaves as FFmpeg's <c>GetByteContext</c>: a read past the end yields zero and leaves
/// the cursor at the end, and a seek is clamped into the range.
/// </summary>
internal struct FmvcReader(byte[] data, int start, int end) {
  private readonly byte[] _data = data;
  private readonly int _start = start;
  private readonly int _end = end;
  private int _at = start;

  internal readonly int Left => this._end - this._at;
  internal readonly int Tell => this._at - this._start;

  internal readonly int Peek() => this._at < this._end ? this._data[this._at] : 0;

  internal int Byte() => this._at < this._end ? this._data[this._at++] : 0;

  internal int UInt16() {
    if (this._end - this._at < 2) {
      this._at = this._end;
      return 0;
    }
    var value = this._data[this._at] | this._data[this._at + 1] << 8;
    this._at += 2;
    return value;
  }

  internal uint UInt32() {
    if (this._end - this._at < 4) {
      this._at = this._end;
      return 0;
    }
    var value = BinaryPrimitives.ReadUInt32LittleEndian(this._data.AsSpan(this._at));
    this._at += 4;
    return value;
  }

  internal void Skip(int count) => this._at += Math.Clamp(count, 0, this._end - this._at);

  internal void Seek(long offset) => this._at = this._start + (int)Math.Clamp(offset, 0, this._end - this._start);
}

/// <summary>A write cursor that behaves as FFmpeg's <c>PutByteContext</c>: once a write would not fit, nothing more is written.</summary>
internal sealed class FmvcWriter(byte[] buffer) {
  internal readonly byte[] Buffer = buffer;
  private int _at;
  private bool _full;

  internal int Tell => this._at;

  internal void Byte(int value) {
    if (!this._full && this._at < this.Buffer.Length)
      this.Buffer[this._at++] = (byte)value;
    else
      this._full = true;
  }

  internal void UInt32(uint value) {
    if (!this._full && this.Buffer.Length - this._at >= 4) {
      BinaryPrimitives.WriteUInt32LittleEndian(this.Buffer.AsSpan(this._at), value);
      this._at += 4;
    } else
      this._full = true;
  }

  /// <summary>A reader over the whole output buffer, written and not, from which back references copy.</summary>
  internal FmvcReader History() => new(this.Buffer, 0, this.Buffer.Length);
}

/// <summary>
/// FM Screen Capture's two decompressors, FFmpeg's <c>decode_type1</c> and <c>decode_type2</c> from
/// <c>libavcodec/fmvc.c</c> (LGPL-2.1-or-later), kept to FFmpeg's control flow: both depend on exactly where
/// a read past the end yields zero and where a back reference is clamped to the buffer's start.
/// </summary>
internal static class FmvcDecompress {

  /// <summary>Type 1: literal runs, raw blocks, and back references of three to eight bytes or of any length.</summary>
  internal static void Type1(ref FmvcReader gb, FmvcWriter pb) {
    var opcode = 0;
    while (gb.Left > 0) {
      FmvcReader gbc;
      int len, pos;
      while (gb.Left > 0) {
        var high = false;
        while (gb.Left > 0) {
          opcode = gb.Byte();
          high = opcode >= 0x20;
          if (high || opcode != 0)
            break;
          opcode = gb.Byte();
          if (opcode < 0xF8) {
            opcode += 32;
            break;
          }
          var i = opcode - 0xF8;
          if (i != 0) {
            len = 256;
            do {
              len *= 2;
              --i;
            } while (i != 0);
          } else
            len = 280;
          do {
            pb.UInt32(gb.UInt32());
            pb.UInt32(gb.UInt32());
            len -= 8;
          } while (len != 0 && gb.Left > 0);
        }

        if (!high) {
          do {
            pb.Byte(gb.Byte());
            --opcode;
          } while (opcode != 0 && gb.Left > 0);

          while (gb.Left > 0) {
            opcode = gb.Byte();
            if (opcode >= 0x20)
              break;
            gbc = pb.History();
            pos = -(opcode | 32 * gb.Byte()) - 1;
            gbc.Seek(pb.Tell + pos);
            pb.Byte(gbc.Byte());
            pb.Byte(gbc.Byte());
            pb.Byte(gbc.Byte());
            pb.Byte(gb.Byte());
          }
        }
        if (opcode < 0x40)
          break;
        gbc = pb.History();
        pos = -((opcode & 0x1F) | 32 * gb.Byte()) - 1;
        gbc.Seek(pb.Tell + pos);
        pb.Byte(gbc.Byte());
        pb.Byte(gbc.Byte());
        len = (opcode >> 5) - 1;
        do {
          pb.Byte(gbc.Byte());
          --len;
        } while (len != 0 && gbc.Left > 0);
      }

      len = opcode & 0x1F;
      if (len == 0) {
        if (gb.Peek() == 0)
          do {
            gb.Skip(1);
            len += 255;
          } while (gb.Peek() == 0 && gb.Left > 0);
        len += gb.Byte() + 31;
      }
      pos = -gb.Byte();
      var history = pb.History();
      history.Seek(pb.Tell + pos - (gb.Byte() << 8));
      if (pb.Tell == history.Tell)
        break; // a back reference of distance zero ends the stream
      if (len < 5 || pb.Tell - history.Tell < 4) {
        pb.Byte(history.Byte());
        pb.Byte(history.Byte());
        pb.Byte(history.Byte());
      } else {
        pb.UInt32(history.UInt32());
        --len;
      }
      do {
        pb.Byte(history.Byte());
        --len;
      } while (len != 0 && history.Left > 0);
    }
  }

  /// <summary>Copies a match the way FFmpeg does: bytewise where it is short or overlaps, else a word first.</summary>
  private static void _CopyMatch(FmvcWriter pb, ref FmvcReader gbc, int len) {
    if (len < 6 || pb.Tell - gbc.Tell < 4) {
      pb.Byte(gbc.Byte());
      pb.Byte(gbc.Byte());
      do {
        pb.Byte(gbc.Byte());
        --len;
      } while (len != 0);
    } else {
      pb.UInt32(gbc.UInt32());
      for (len -= 2; len != 0; --len)
        pb.Byte(gbc.Byte());
    }
  }

  private static void _Literals(ref FmvcReader gb, FmvcWriter pb, int len) {
    do {
      pb.Byte(gb.Byte());
      --len;
    } while (len != 0);
  }

  /// <summary>The three bytes FFmpeg copies from more than 2 KiB back after a literal run.</summary>
  private static void _FarThree(ref FmvcReader gb, FmvcWriter pb, int opcode) {
    gb.Skip(1);
    var pos = -(opcode >> 2) - 4 * gb.Byte() - 2049;
    var gbc = pb.History();
    gbc.Seek(pb.Tell + pos);
    pb.Byte(gbc.Byte());
    pb.Byte(gbc.Byte());
    pb.Byte(gbc.Byte());
  }

  /// <summary>Type 2, an LZO1X stream.</summary>
  internal static void Type2(ref FmvcReader gb, FmvcWriter pb) {
    bool repeat = false, first = true;
    var opcode = 0;
    int len, pos, i;
    while (gb.Left > 0) {
      FmvcReader gbc;
      while (gb.Left > 0) {
        if (first) {
          first = false;
          if (gb.Peek() > 17) {
            len = gb.Byte() - 17;
            if (len < 4) {
              _Literals(ref gb, pb, len);
              opcode = gb.Peek();
              continue;
            }
            _Literals(ref gb, pb, len);
            opcode = gb.Peek();
            if (opcode < 0x10) {
              _FarThree(ref gb, pb, opcode);
              len = opcode & 3;
              if (len == 0)
                repeat = true;
              else {
                _Literals(ref gb, pb, len);
                opcode = gb.Peek();
              }
              continue;
            }
          }
          repeat = true;
        }
        if (repeat) {
          repeat = false;
          opcode = gb.Peek();
          if (opcode < 0x10) {
            gb.Skip(1);
            if (opcode == 0) {
              if (gb.Peek() == 0)
                do {
                  gb.Skip(1);
                  opcode += 255;
                } while (gb.Peek() == 0 && gb.Left > 0);
              opcode += gb.Byte() + 15;
            }
            pb.UInt32(gb.UInt32());
            for (i = opcode - 1; i > 0; --i)
              pb.Byte(gb.Byte());
            opcode = gb.Peek();
            if (opcode < 0x10) {
              _FarThree(ref gb, pb, opcode);
              len = opcode & 3;
              if (len == 0)
                repeat = true;
              else {
                _Literals(ref gb, pb, len);
                opcode = gb.Peek();
              }
              continue;
            }
          }
        }

        if (opcode >= 0x40) {
          gb.Skip(1);
          pos = -((opcode >> 2) & 7) - 1 - 8 * gb.Byte();
          len = (opcode >> 5) - 1;
          gbc = pb.History();
          gbc.Seek(pb.Tell + pos);
          pb.Byte(gbc.Byte());
          pb.Byte(gbc.Byte());
          do {
            pb.Byte(gbc.Byte());
            --len;
          } while (len != 0);
          len = opcode & 3;
          if (len == 0)
            repeat = true;
          else {
            _Literals(ref gb, pb, len);
            opcode = gb.Peek();
          }
          continue;
        }
        if (opcode < 0x20)
          break;
        len = opcode & 0x1F;
        gb.Skip(1);
        if (len == 0) {
          if (gb.Peek() == 0)
            do {
              gb.Skip(1);
              len += 255;
            } while (gb.Peek() == 0 && gb.Left > 0);
          len += gb.Byte() + 31;
        }
        i = gb.UInt16();
        pos = -(i >> 2) - 1;
        gbc = pb.History();
        gbc.Seek(pb.Tell + pos);
        _CopyMatch(pb, ref gbc, len);
        len = i & 3;
        if (len == 0)
          repeat = true;
        else {
          _Literals(ref gb, pb, len);
          opcode = gb.Peek();
        }
      }

      gb.Skip(1);
      if (opcode < 0x10) {
        pos = -(opcode >> 2) - 1 - 4 * gb.Byte();
        gbc = pb.History();
        gbc.Seek(pb.Tell + pos);
        pb.Byte(gbc.Byte());
        pb.Byte(gbc.Byte());
        len = opcode & 3;
        if (len == 0)
          repeat = true;
        else {
          _Literals(ref gb, pb, len);
          opcode = gb.Peek();
        }
        continue;
      }
      len = opcode & 7;
      if (len == 0) {
        if (gb.Peek() == 0)
          do {
            gb.Skip(1);
            len += 255;
          } while (gb.Peek() == 0 && gb.Left > 0);
        len += gb.Byte() + 7;
      }
      i = gb.UInt16();
      pos = pb.Tell - 2048 * (opcode & 8);
      pos -= i >> 2;
      if (pos == pb.Tell)
        break; // the end of the stream
      pos -= 0x4000;
      gbc = pb.History();
      gbc.Seek(pos);
      _CopyMatch(pb, ref gbc, len);
      len = i & 3;
      if (len == 0)
        repeat = true;
      else {
        _Literals(ref gb, pb, len);
        opcode = gb.Peek();
      }
    }
  }
}

/// <summary>Compresses into type 1: literal runs and raw blocks between back references found by a hash chain.</summary>
internal static class FmvcCompress {

  private const int _WINDOW = 0xFFFF;
  private const int _CHAIN = 32;
  private const int _MINIMUM = 4;

  internal static byte[] Type1(ReadOnlySpan<byte> data) {
    var output = new List<byte>();
    var head = new int[1 << 16];
    Array.Fill(head, -1);
    var previous = new int[data.Length];
    var literalStart = 0;
    var at = 0;
    while (at < data.Length) {
      var (distance, length) = _Find(data, at, head, previous);
      if (length < _MINIMUM) {
        _Insert(data, at, head, previous);
        ++at;
        continue;
      }
      _Literals(output, data[literalStart..at]);
      _Match(output, distance, length);
      for (var k = 0; k < length; ++k)
        _Insert(data, at + k, head, previous);
      at += length;
      literalStart = at;
    }
    _Literals(output, data[literalStart..]);
    output.AddRange([0x21, 0, 0]); // a long back reference of distance zero: the end
    return [.. output];
  }

  private static int _Hash(ReadOnlySpan<byte> data, int at) => (int)(((uint)(data[at] | data[at + 1] << 8 | data[at + 2] << 16) * 2654435761u) >> 16);

  private static void _Insert(ReadOnlySpan<byte> data, int at, int[] head, int[] previous) {
    if (at + 3 > data.Length)
      return;
    var hash = _Hash(data, at);
    previous[at] = head[hash];
    head[hash] = at;
  }

  private static (int Distance, int Length) _Find(ReadOnlySpan<byte> data, int at, int[] head, int[] previous) {
    if (at + 3 > data.Length)
      return (0, 0);
    int best = 0, bestDistance = 0, chain = _CHAIN;
    for (var candidate = head[_Hash(data, at)]; candidate >= 0 && chain-- > 0; candidate = previous[candidate]) {
      var distance = at - candidate;
      if (distance > _WINDOW)
        break;
      var length = data[at..].CommonPrefixLength(data[candidate..]);
      // An overlapping copy repeats the distance's worth of bytes, so the match can run past at.
      if (length > best) {
        (best, bestDistance) = (length, distance);
      }
    }
    return (bestDistance, best);
  }

  /// <summary>
  /// Literals as raw blocks of 280 bytes while more remain than a run holds, then one run; a run must be
  /// followed by a back reference, which every caller supplies.
  /// </summary>
  private static void _Literals(List<byte> output, ReadOnlySpan<byte> literals) {
    while (literals.Length > 279) {
      output.AddRange([0x00, 0xF8]);
      foreach (var b in literals[..280])
        output.Add(b);
      literals = literals[280..];
    }
    if (literals.IsEmpty)
      return;
    if (literals.Length < 32)
      output.Add((byte)literals.Length);
    else
      output.AddRange([0x00, (byte)(literals.Length - 32)]);
    foreach (var b in literals)
      output.Add(b);
  }

  /// <summary>A back reference: three to eight bytes within 8 KiB as one opcode and a byte, else the long form.</summary>
  private static void _Match(List<byte> output, int distance, int length) {
    if (length <= 8 && distance <= 8192) {
      var d = distance - 1;
      output.Add((byte)(((length - 1) << 5) | (d & 0x1F)));
      output.Add((byte)(d >> 5));
      return;
    }
    var n = length - 3;
    if (n <= 31)
      output.Add((byte)(0x20 | n));
    else {
      output.Add(0x20);
      var rest = n - 31;
      while (rest > 255) {
        output.Add(0);
        rest -= 255;
      }
      output.Add((byte)rest);
    }
    output.Add((byte)distance);
    output.Add((byte)(distance >> 8));
  }
}

/// <summary>
/// The FM Screen Capture picture: a bottom-up buffer of 32-bit-aligned rows that key frames replace and
/// other frames change by XOR, block by block, and the blocks' layout.
/// </summary>
internal sealed class FmvcFrame {

  internal const int BlockHeight = 112;
  internal const int BlockWidth = 84;

  internal readonly int Width;
  internal readonly int Height;
  internal readonly int BytesPerPixel;
  /// <summary>Row length in 32-bit words.</summary>
  internal readonly int Stride;
  internal readonly int BlocksWide;
  internal readonly int BlocksHigh;
  internal readonly (int W, int H)[] Blocks;
  internal readonly byte[] Buffer;
  private readonly byte[] _deltas;

  internal FmvcFrame(int width, int height, int bitsPerPixel) {
    this.Width = width;
    this.Height = height;
    this.BytesPerPixel = bitsPerPixel >> 3;
    this.Stride = (width * bitsPerPixel + 31) / 32;
    int w = BlockWidth, h = BlockHeight;
    this.BlocksWide = this.Stride / BlockWidth;
    var m = this.Stride % BlockWidth;
    if (m != 0) {
      if (m < 37)
        w = m + BlockWidth;
      else {
        w = m;
        ++this.BlocksWide;
      }
    }
    this.BlocksHigh = height / BlockHeight;
    m = height % BlockHeight;
    if (m != 0) {
      if (m < 49)
        h = m + BlockHeight;
      else {
        h = m;
        ++this.BlocksHigh;
      }
    }
    if (this.BlocksWide * this.BlocksHigh == 0)
      throw new NotSupportedException(
        $"FM Screen Capture divides a picture into blocks of at least 37 words by 49 rows, and FFmpeg refuses one too small for any; {width}x{height} at {bitsPerPixel} bits is.");
    this.Blocks = new (int, int)[this.BlocksWide * this.BlocksHigh];
    var block = 0;
    for (var i = 0; i < this.BlocksHigh; ++i)
      for (var j = 0; j < this.BlocksWide; ++j)
        this.Blocks[block++] = (j == this.BlocksWide - 1 ? w : BlockWidth, i == this.BlocksHigh - 1 ? h : BlockHeight);
    this.Buffer = new byte[width * height * 4];
    this._deltas = new byte[width * height * 4];
  }

  /// <summary>The offset in the buffer of each block's first word, in block order.</summary>
  internal int BlockOffset(int index) {
    int row = index / this.BlocksWide, column = index % this.BlocksWide;
    var y = row * BlockHeight;
    var x = column * BlockWidth;
    return (y * this.Stride + x) * 4;
  }

  internal void Decode(ReadOnlySpan<byte> packet) {
    if (packet.Length < 8)
      throw new InvalidDataException($"An FM Screen Capture frame is at least eight bytes; this one is {packet.Length}.");
    var data = packet.ToArray();
    var gb = new FmvcReader(data, 0, data.Length);
    gb.Skip(2);
    if (gb.UInt16() != 0) {
      var type = gb.UInt16();
      var size = gb.UInt16();
      if (size > gb.Left)
        throw new InvalidDataException("This FM Screen Capture key frame states more data than it holds.");
      var pb = new FmvcWriter(this.Buffer);
      _Decompress(type, ref gb, pb);
      return;
    }

    var count = gb.UInt16();
    if (count > this.Blocks.Length)
      throw new InvalidDataException($"This FM Screen Capture frame changes {count} blocks of {this.Blocks.Length}.");
    var deltas = new FmvcWriter(this._deltas);
    var kind = gb.UInt16();
    var changed = new bool[this.Blocks.Length];
    for (var b = 0; b < count; ++b) {
      var offset = gb.UInt16();
      if (offset >= this.Blocks.Length)
        throw new InvalidDataException($"This FM Screen Capture frame names block {offset} of {this.Blocks.Length}.");
      var size = gb.UInt16();
      if (size > gb.Left)
        throw new InvalidDataException("This FM Screen Capture frame states more block data than it holds.");
      var start = deltas.Tell;
      _Decompress(kind, ref gb, deltas);
      var (w, h) = this.Blocks[offset];
      if (w * h * 4 != deltas.Tell - start)
        throw new InvalidDataException($"FM Screen Capture block {offset} unpacks to {deltas.Tell - start} bytes, not its {w * h * 4}.");
      changed[offset] = true;
    }

    // FFmpeg's walk: blocks in order, each one's changed words XORed in from the deltas as they come.
    var source = 0;
    for (var b = 0; b < this.Blocks.Length; ++b) {
      if (!changed[b])
        continue;
      var (w, h) = this.Blocks[b];
      var origin = this.BlockOffset(b);
      for (var k = 0; k < h; ++k)
        for (var l = 0; l < w * 4; ++l)
          this.Buffer[origin + k * this.Stride * 4 + l] ^= this._deltas[source++];
    }
  }

  private static void _Decompress(int type, ref FmvcReader gb, FmvcWriter pb) {
    if (type == 1)
      FmvcDecompress.Type1(ref gb, pb);
    else if (type == 2)
      FmvcDecompress.Type2(ref gb, pb);
    else
      throw new NotSupportedException($"FM Screen Capture compression type {type} is not one FFmpeg reads either.");
  }

  /// <summary>The picture top row first, as the buffer holds it bottom row first.</summary>
  internal byte[] Picture() {
    var row = this.Width * this.BytesPerPixel;
    var picture = new byte[row * this.Height];
    for (var y = 0; y < this.Height; ++y)
      this.Buffer.AsSpan((this.Height - 1 - y) * this.Stride * 4, row).CopyTo(picture.AsSpan(y * row));
    return picture;
  }
}

/// <summary>Writes FM Screen Capture frames: a key frame first, then the changed blocks' XOR, all in type 1.</summary>
internal sealed class FmvcEncoder(int width, int height) {

  private const int _MAGIC = 0xC8;
  private readonly FmvcFrame _layout = new(width, height, 24);
  private byte[]? _previous;

  internal byte[] Encode(ReadOnlySpan<byte> bgr) {
    var layout = this._layout;
    var rowBytes = layout.Stride * 4;
    var current = new byte[rowBytes * height];
    for (var y = 0; y < height; ++y)
      bgr.Slice(y * width * 3, width * 3).CopyTo(current.AsSpan((height - 1 - y) * rowBytes));

    using var output = new MemoryStream();
    void Put(int value) {
      output.WriteByte((byte)value);
      output.WriteByte((byte)(value >> 8));
    }

    Put(_MAGIC);
    if (this._previous == null) {
      var packed = FmvcCompress.Type1(current);
      Put(1);
      Put(1);
      Put(Math.Min(packed.Length, 0xFFFF));
      output.Write(packed);
    } else {
      var changed = new List<(int Index, byte[] Data)>();
      for (var b = 0; b < layout.Blocks.Length; ++b) {
        var (w, h) = layout.Blocks[b];
        var origin = layout.BlockOffset(b);
        var delta = new byte[w * h * 4];
        var any = false;
        for (var k = 0; k < h; ++k)
          for (var l = 0; l < w * 4; ++l) {
            var at = origin + k * rowBytes + l;
            var x = (byte)(current[at] ^ this._previous[at]);
            delta[k * w * 4 + l] = x;
            any |= x != 0;
          }
        if (any)
          changed.Add((b, FmvcCompress.Type1(delta)));
      }
      Put(0);
      Put(changed.Count);
      Put(1);
      foreach (var (index, data) in changed) {
        Put(index);
        Put(Math.Min(data.Length, 0xFFFF));
        output.Write(data);
      }
    }
    this._previous = current;
    return output.ToArray();
  }
}
