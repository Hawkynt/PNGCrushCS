using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Codecs.Anm;

/// <summary>
/// The Deluxe Paint Animation picture decoder, FFmpeg's <c>libavcodec/anm.c</c> (LGPL-2.1-or-later): one
/// eight-bit picture that every record rewrites with RunSkipDump operations.
/// </summary>
/// <remarks>
/// A record is <c>0x42</c>, a zero, two bytes nobody reads, and then operations: a byte whose low seven
/// bits are a count dumps that many bytes, or with the top bit set skips that many pixels; a zero byte
/// is followed by a count and a pixel to fill with; <c>0x80</c> is followed by a sixteen-bit word whose top
/// two bits choose a skip, a longer skip of 0x4000 more, a dump or a fill, and whose zero count with type
/// zero ends the record. Operations run along the picture line by line; one that would pass the end of
/// the picture, or a dump with too few bytes left, ends the record where it stands.
/// </remarks>
internal sealed class AnmFrameDecoder(int width, int height) {

  internal const int RecordType = 0x42;

  private readonly byte[] _picture = new byte[width * height];

  public byte[] Picture => this._picture;

  /// <summary>Applies one record. False for an empty record, which FFmpeg passes over without a picture.</summary>
  public bool Decode(ReadOnlySpan<byte> record) {
    if (record.Length == 0)
      return false;
    if (record.Length < 7)
      throw new InvalidDataException($"A Deluxe Paint Animation record is at least seven bytes; this one is {record.Length}.");
    if (record[0] != RecordType)
      throw new InvalidDataException($"This Deluxe Paint Animation record is of type 0x{record[0]:X2}; only 0x42, a RunSkipDump picture, is defined.");
    if (record[1] != 0)
      throw new NotSupportedException("This Deluxe Paint Animation record states padding bytes, which no known file uses.");

    var at = 4;
    var position = 0;
    do {
      var type = at < record.Length ? record[at++] : 0;
      var count = type & 0x7F;
      if (count != 0) {
        if (this._Op(record, ref at, (type & 0x80) != 0 ? _Skip : _Dump, 0, count, ref position))
          break;
      } else if ((type & 0x80) == 0) {
        count = at < record.Length ? record[at++] : 0; // a count of zero is no operation
        var pixel = at < record.Length ? record[at++] : 0;
        if (this._Op(record, ref at, _Fill, pixel, count, ref position))
          break;
      } else {
        var word = 0;
        if (record.Length - at >= 2) {
          word = record[at] | record[at + 1] << 8;
          at += 2;
        } else
          at = record.Length;
        count = word & 0x3FFF;
        var kind = word >> 14;
        if (count == 0) {
          if (kind == 0)
            break; // the end of the record
          if (kind == 2)
            throw new NotSupportedException("This Deluxe Paint Animation record uses the undefined opcode 0x8000.");
          continue;
        }
        var pixel = 0;
        if (kind == 3)
          pixel = at < record.Length ? record[at++] : 0;
        if (kind == 1)
          count += 0x4000;
        var mode = kind switch { 2 => _Dump, 3 => _Fill, _ => _Skip };
        if (this._Op(record, ref at, mode, pixel, count, ref position))
          break;
      }
    } while (at < record.Length);
    return true;
  }

  private const int _Dump = 0, _Fill = 1, _Skip = 2;

  /// <summary>FFmpeg's <c>op</c>: works through the picture a line at a time; true once the picture or the record is exhausted.</summary>
  private bool _Op(ReadOnlySpan<byte> record, ref int at, int mode, int pixel, int count, ref int position) {
    var end = this._picture.Length;
    var remaining = width - position % width;
    while (count > 0) {
      var strip = Math.Min(count, remaining);
      if (mode == _Dump) {
        if (record.Length - at < strip)
          return true;
        record.Slice(at, strip).CopyTo(this._picture.AsSpan(position));
        at += strip;
      } else if (mode == _Fill)
        this._picture.AsSpan(position, strip).Fill((byte)pixel);
      position += strip;
      remaining -= strip;
      count -= strip;
      if (remaining <= 0)
        remaining = width;
      if (position >= end)
        return true;
    }
    return false;
  }
}

/// <summary>
/// Writes RunSkipDump records: pixels unchanged since the last picture skipped, runs of one value filled,
/// and the rest dumped.
/// </summary>
internal sealed class AnmEncoder(int width, int height) {

  private const int _LONG = 0x3FFF;

  private readonly byte[] _previous = new byte[width * height];
  private bool _started;

  public byte[] Encode(ReadOnlySpan<byte> picture) {
    var output = new List<byte> { AnmFrameDecoder.RecordType, 0, 1, 0 };
    var size = picture.Length;
    for (var at = 0; at < size;) {
      var unchanged = this._started ? _Unchanged(picture, at) : 0;
      if (unchanged > 0 && (unchanged >= 2 || at + unchanged == size)) {
        if (at + unchanged == size)
          break; // nothing after this changes; the record can simply end
        _Skip(output, unchanged);
        at += unchanged;
        continue;
      }

      var run = _Run(picture, at);
      if (run >= 3) {
        _Fill(output, picture[at], run);
        at += run;
        continue;
      }

      // Dump up to where a skip or a fill would pay for itself.
      var start = at;
      while (at < size && at - start < _LONG) {
        if (this._started && _Unchanged(picture, at) >= 3)
          break;
        if (_Run(picture, at) >= 4)
          break;
        ++at;
      }
      if (at == start)
        ++at;
      _Dump(output, picture[start..at]);
    }
    output.AddRange([0x80, 0, 0]);
    picture.CopyTo(this._previous);
    this._started = true;
    return [.. output];
  }

  private int _Unchanged(ReadOnlySpan<byte> picture, int at) => picture[at..].CommonPrefixLength(this._previous.AsSpan(at));

  private static int _Run(ReadOnlySpan<byte> picture, int at) {
    var length = 1;
    while (at + length < picture.Length && picture[at + length] == picture[at] && length < _LONG)
      ++length;
    return length;
  }

  private static void _Skip(List<byte> output, int count) {
    while (count > 0) {
      int step;
      if (count <= 0x7F) {
        step = count;
        output.Add((byte)(0x80 | step));
      } else if (count <= _LONG || count == 0x4000) {
        step = Math.Min(count, _LONG);
        output.AddRange([0x80, (byte)step, (byte)(step >> 8)]);
      } else {
        // The longer skip adds 0x4000 to a count that must not itself be zero.
        step = Math.Min(count, 0x7FFF);
        var word = 0x4000 | (step - 0x4000);
        output.AddRange([0x80, (byte)word, (byte)(word >> 8)]);
      }
      count -= step;
    }
  }

  private static void _Fill(List<byte> output, byte pixel, int count) {
    if (count <= 0xFF)
      output.AddRange([0, (byte)count, pixel]);
    else {
      var word = 0xC000 | count;
      output.AddRange([0x80, (byte)word, (byte)(word >> 8), pixel]);
    }
  }

  private static void _Dump(List<byte> output, ReadOnlySpan<byte> pixels) {
    if (pixels.Length <= 0x7F)
      output.Add((byte)pixels.Length);
    else {
      var word = 0x8000 | pixels.Length;
      output.AddRange([0x80, (byte)word, (byte)(word >> 8)]);
    }
    foreach (var pixel in pixels)
      output.Add(pixel);
  }
}
