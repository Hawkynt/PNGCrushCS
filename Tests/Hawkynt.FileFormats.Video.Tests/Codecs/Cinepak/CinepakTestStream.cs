using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.Cinepak.Tests;

/// <summary>
/// Builds Cinepak frames byte by byte, so the decoder can be tested without a sample file in the
/// tree.
/// </summary>
/// <remarks>
/// Everything Cinepak states is big-endian, which is the one thing about the format that a reader
/// written on a little-endian machine gets wrong silently — a length read the wrong way round is
/// still a plausible length. Building the streams here the same way round they are in a file is what
/// makes these tests able to catch that.
/// </remarks>
internal static class CinepakTestStream {

  internal const int StripIntra = 0x1000;
  internal const int StripInter = 0x1100;

  /// <summary>Assembles a frame around its strips.</summary>
  internal static byte[] Frame(int flags, int width, int height, params byte[][] strips) {
    var body = _Concat(strips);
    var frame = new byte[10 + body.Length];

    frame[0] = (byte)flags;
    frame[1] = (byte)(frame.Length >> 16);
    frame[2] = (byte)(frame.Length >> 8);
    frame[3] = (byte)frame.Length;
    _WriteBe16(frame, 4, width);
    _WriteBe16(frame, 6, height);
    _WriteBe16(frame, 8, strips.Length);
    body.CopyTo(frame, 10);

    return frame;
  }

  /// <summary>Assembles a strip around its chunks, with the four coordinates written verbatim.</summary>
  internal static byte[] Strip(int identifier, int top, int left, int bottom, int right, params byte[][] chunks) {
    var body = _Concat(chunks);
    var strip = new byte[12 + body.Length];

    _WriteBe16(strip, 0, identifier);
    _WriteBe16(strip, 2, strip.Length);
    _WriteBe16(strip, 4, top);
    _WriteBe16(strip, 6, left);
    _WriteBe16(strip, 8, bottom);
    _WriteBe16(strip, 10, right);
    body.CopyTo(strip, 12);

    return strip;
  }

  /// <summary>A chunk, whose stated length includes its own four-byte header.</summary>
  internal static byte[] Chunk(int identifier, params byte[] body) {
    var chunk = new byte[4 + body.Length];

    _WriteBe16(chunk, 0, identifier);
    _WriteBe16(chunk, 2, chunk.Length);
    body.CopyTo(chunk, 4);

    return chunk;
  }

  /// <summary>A codebook stated in full, from entry zero.</summary>
  internal static byte[] Codebook(int identifier, params byte[][] entries)
    => Chunk(identifier, _Concat(entries));

  /// <summary>
  /// A codebook update: a word of flags for every thirty-two entries, with the changed entries'
  /// bodies immediately behind each word.
  /// </summary>
  internal static byte[] CodebookUpdate(int identifier, int entryCount, Func<int, byte[]?> entry) {
    var body = new List<byte>();

    for (var first = 0; first < entryCount; first += 32) {
      var bodies = new List<byte[]>();
      var flags = 0u;
      for (var bit = 0; bit < 32 && first + bit < entryCount; ++bit) {
        var changed = entry(first + bit);
        if (changed == null)
          continue;

        flags |= 0x80000000u >> bit;
        bodies.Add(changed);
      }

      body.AddRange([(byte)(flags >> 24), (byte)(flags >> 16), (byte)(flags >> 8), (byte)flags]);
      foreach (var one in bodies)
        body.AddRange(one);
    }

    return Chunk(identifier, body.ToArray());
  }

  /// <summary>One codebook entry with chrominance, whose two chrominance bytes are signed.</summary>
  internal static byte[] Entry(byte y0, byte y1, byte y2, byte y3, sbyte u, sbyte v)
    => [y0, y1, y2, y3, (byte)u, (byte)v];

  /// <summary>One codebook entry with no chrominance at all.</summary>
  internal static byte[] GreyEntry(byte y0, byte y1, byte y2, byte y3) => [y0, y1, y2, y3];

  /// <summary>
  /// An intra vector list (0x3000): one flag bit a block, set for V4, with the references behind each
  /// word of flags.
  /// </summary>
  internal static byte[] IntraVectors(params byte[]?[] blocks) {
    var writer = new VectorWriter();

    foreach (var block in blocks) {
      if (block!.Length is not (1 or 4))
        throw new ArgumentException("A block is one reference or four.", nameof(blocks));

      writer.Bit(block.Length == 4);
      writer.References(block);
    }

    return Chunk(0x3000, writer.Finish());
  }

  /// <summary>
  /// An inter vector list (0x3100): a bit a block for skipped, two for coded, with the bits running
  /// on across word boundaries wherever they fall.
  /// </summary>
  /// <param name="blocks">One entry a block: <c>null</c> to skip, one reference for V1, four for V4.</param>
  internal static byte[] InterVectors(params byte[]?[] blocks) {
    var writer = new VectorWriter();

    foreach (var block in blocks) {
      if (block == null) {
        writer.Bit(false);
        continue;
      }

      writer.Bit(true);
      writer.Bit(block.Length == 4);
      writer.References(block);
    }

    return Chunk(0x3100, writer.Finish());
  }

  /// <summary>
  /// Writes a vector list, putting each word of flags exactly where a reader would ask for one.
  /// </summary>
  /// <remarks>
  /// A word is not written when the previous one fills; it is written when the next bit is wanted and
  /// there is none left. The two differ for a block whose last flag bit is the last of a word — its
  /// references come before the next word under the first rule and after it under the second — and a
  /// reader and a writer that disagree here are out of step for the rest of the chunk.
  /// <para/>
  /// So the word is reserved as four zero bytes and its bits are set in place afterwards, which is
  /// the only way for a writer to put it where a reader that refills lazily will look.
  /// </remarks>
  private sealed class VectorWriter {

    private readonly List<byte> _body = [];
    private int _wordAt = -1;
    private int _used = 32;

    internal void Bit(bool set) {
      if (this._used == 32) {
        this._wordAt = this._body.Count;
        this._body.AddRange([(byte)0, (byte)0, (byte)0, (byte)0]);
        this._used = 0;
      }

      if (set)
        this._body[this._wordAt + this._used / 8] |= (byte)(1 << (7 - this._used % 8));

      ++this._used;
    }

    internal void References(byte[] references) => this._body.AddRange(references);

    internal byte[] Finish() => this._body.ToArray();
  }

  /// <summary>A V1-only vector list (0x3200): one reference a block and no flags at all.</summary>
  internal static byte[] V1Vectors(params byte[] references) => Chunk(0x3200, references);

  private static void _WriteBe16(byte[] into, int at, int value) {
    into[at] = (byte)(value >> 8);
    into[at + 1] = (byte)value;
  }

  private static byte[] _Concat(IReadOnlyList<byte[]> parts) {
    var length = 0;
    foreach (var part in parts)
      length += part.Length;

    var all = new byte[length];
    var at = 0;
    foreach (var part in parts) {
      part.CopyTo(all, at);
      at += part.Length;
    }

    return all;
  }
}
