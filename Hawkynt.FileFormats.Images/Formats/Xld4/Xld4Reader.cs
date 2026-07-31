using System;
using System.IO;

namespace FileFormat.Xld4;

/// <summary>Reads XLD4 pictures from bytes, streams, or file paths.</summary>
public static class Xld4Reader {

  /// <summary>Symbols the dictionary coder starts with: the sixteen colours and one more.</summary>
  private const int _LITERALS = 17;

  /// <summary>Dictionary entries the coder may reach, which is what fifteen-bit codes allow.</summary>
  private const int _MAX_CODES = 16384;

  /// <summary>The largest a chunk may unpack to.</summary>
  private const int _MAX_UNPACKED = 65536;

  /// <summary>The base a run length is written in, there being seventeen symbols to write it with.</summary>
  private const int _LENGTH_BASE = 17;

  public static Xld4File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static Xld4File FromStream(Stream stream) {
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

  public static Xld4File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 22 || (data[2] != 2 && (data[1] > 1 || data[3] > 1))
        || data[8] + (data[9] << 8) != data.Length
        || data[11] != 'M' || data[12] != 'A' || data[13] != 'J' || data[14] != 'Y' || data[15] != 'O')
      throw new InvalidDataException("Not an XLD4 picture.");

    var stream = new _Stream(data);
    var nextChunk = stream.StartChunk(16);
    stream.Unpack();

    var palette = new byte[Xld4File.ColorCount * 3];
    for (var i = 0; i < Xld4File.ColorCount; ++i) {
      var color = 0;
      for (var channel = 0; channel < 3; ++channel) {
        // Two symbols a channel, of which the first is the intensity the program's own editor
        // showed and the second the one it wrote.
        if (stream.Read() < 0)
          throw new InvalidDataException($"Palette entry {i} is incomplete.");

        var value = stream.Read();
        if (value < 0)
          throw new InvalidDataException($"Palette entry {i} is incomplete.");

        color = (color << 8) | (value * 17);
      }

      // The palette is stored in the order the hardware's registers are addressed, which is
      // neither the index order nor a simple reversal.
      var target = ((i & 8) | ((i & 1) << 2) | ((i >> 1) & 3)) * 3;
      palette[target] = (byte)(color >> 16);
      palette[target + 1] = (byte)(color >> 8);
      palette[target + 2] = (byte)color;
    }

    var pixels = new byte[Xld4File.Width * Xld4File.Height];
    var left = 0;

    for (var i = 0; i < pixels.Length; ++i) {
      if (--left <= 0) {
        if (nextChunk >= data.Length - 6)
          throw new InvalidDataException("An XLD4 picture ends before its chunks do.");

        // A chunk says how many pixels it covers before the dictionary is even started.
        left = (data[nextChunk + 4] | (data[nextChunk + 5] << 8)) << 1;
        nextChunk = stream.StartChunk(nextChunk);
        stream.Unpack();
      }

      pixels[i] = (byte)Math.Max(stream.Read(), 0);
    }

    return new() { Pixels = pixels, Palette = palette };
  }

  /// <summary>The two layers a chunk is packed in, read one after the other.</summary>
  private ref struct _Stream {

    private readonly ReadOnlySpan<byte> _file;
    private readonly byte[] _unpacked = new byte[_MAX_UNPACKED];
    private int _at;
    private int _limit;
    private int _bits;
    private int _codeBits;
    private int _unpackedLength;
    private int _readAt;
    private int _count;
    private int _value;
    private int _lastValue;

    public _Stream(ReadOnlySpan<byte> file) {
      this._file = file;
    }

    /// <summary>Steps to a chunk's body and returns where the chunk ends.</summary>
    public int StartChunk(int offset) {
      if (offset + 6 > this._file.Length)
        throw new InvalidDataException("An XLD4 chunk has no header.");

      var length = this._file[offset] | (this._file[offset + 1] << 8);
      this._at = offset + 6;
      this._limit = this._at + length;

      return this._limit;
    }

    private int _PackedBit() {
      if ((this._bits & 127) == 0) {
        if (this._at >= this._limit || this._at >= this._file.Length)
          return -1;

        this._bits = (this._file[this._at++] << 1) | 1;
      } else
        this._bits <<= 1;

      return (this._bits >> 8) & 1;
    }

    private int _PackedBits(int count) {
      var result = 0;
      while (--count >= 0) {
        var bit = this._PackedBit();
        if (bit < 0)
          return -1;

        result = (result << 1) | bit;
      }

      return result;
    }

    /// <summary>
    /// Reads one dictionary code. A code of one is not a code but an instruction to read the next
    /// one a bit wider, which is how the width grows without either side counting entries.
    /// </summary>
    private int _Code() {
      do {
        var value = this._PackedBits(this._codeBits);
        switch (value) {
          case -1 or 0: return -1;
          case 1: break;
          default: return value - 2;
        }
      } while (++this._codeBits <= 15);

      return -1;
    }

    /// <summary>Unpacks the current chunk's dictionary coding into the buffer the runs read from.</summary>
    public void Unpack() {
      this._bits = 0;
      this._codeBits = 3;
      this._unpackedLength = 0;

      var offsets = new int[_MAX_CODES + 1];

      for (var codes = _LITERALS; codes < _MAX_CODES; ++codes) {
        var code = this._Code();

        // A code the dictionary has not defined yet ends the chunk; there is no explicit marker.
        if (code < 0 || code >= codes)
          break;

        if (this._unpackedLength >= _MAX_UNPACKED)
          throw new InvalidDataException("An XLD4 chunk unpacks to more than it may.");

        offsets[codes] = this._unpackedLength;

        if (code < _LITERALS) {
          this._unpacked[this._unpackedLength++] = (byte)code;
          continue;
        }

        // An entry is the one it names plus the symbol after it — and where that symbol is the one
        // about to be written, the copy catches up with itself, which is how a run of the same
        // pair is coded.
        var from = offsets[code];
        var to = offsets[code + 1];
        if (this._unpackedLength + to - from >= _MAX_UNPACKED)
          throw new InvalidDataException("An XLD4 chunk unpacks to more than it may.");

        do
          this._unpacked[this._unpackedLength++] = this._unpacked[from++];
        while (from <= to);
      }

      this._readAt = 0;

      // The run in progress is not cleared: the chunks are one run-length stream between them, and
      // a run that reaches the end of a chunk carries its remaining count into the next.
      this._lastValue = 0;
    }

    private int _Symbol() => this._readAt < this._unpackedLength ? this._unpacked[this._readAt++] : -1;

    /// <summary>Reads one colour index from the run-length layer.</summary>
    public int Read() {
      while (this._count == 0) {
        var b = this._Symbol();
        if (b < 0)
          return -1;

        // A symbol below sixteen is a colour standing for itself; the seventeenth introduces a run.
        if (b < 16) {
          this._count = 1;
          this._value = b;
          break;
        }

        b = this._Symbol();

        // A zero where the length's high digit belongs means the run's colour changes first.
        if (b == 0) {
          this._lastValue = this._Symbol();
          if (this._lastValue is < 0 or >= 16)
            return -1;

          b = this._Symbol();
        }

        if (b < 0)
          return -1;

        var low = this._Symbol();
        if (low < 0)
          return -1;

        // Two digits in base seventeen, there being seventeen symbols to write them with.
        this._count = b * _LENGTH_BASE + low;
        this._value = this._lastValue;
      }

      --this._count;

      return this._value;
    }
  }

  public static Xld4File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
