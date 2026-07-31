using System;
using System.IO;

namespace FileFormat.ArtMaster88;

/// <summary>Reads Art Master 88 pictures from bytes, streams, or file paths.</summary>
public static class ArtMaster88Reader {

  /// <summary>What every file begins with.</summary>
  private const string _SIGNATURE = "SS_SIF    0.0";

  public static ArtMaster88File FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static ArtMaster88File FromStream(Stream stream) {
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

  public static ArtMaster88File FromSpan(ReadOnlySpan<byte> data) {
    if (data.Length < 42 || !_IsStringAt(data, 0, _SIGNATURE)
        || data[19] != 'B' || data[20] != 'R' || data[21] != 'G' || data[24] != 128 || data[25] != 2)
      throw new InvalidDataException("Not an Art Master 88 picture.");

    var stream = new _Stream(data, 40);

    // Letters in the header say which optional chunks are present; each carries its own length and
    // is stepped over rather than read.
    if (data[16] == 'I')
      stream.SkipChunk();

    var height = data[26] | (data[27] << 8);

    return height switch {
      200 => _Pc88(data, ref stream),
      400 => _Pc98(data, ref stream),
      _ => throw new InvalidDataException($"A picture {height} rows tall is not one Art Master wrote."),
    };
  }

  /// <summary>The PC-88 form: three planes that are the three channels, so there is no palette.</summary>
  private static ArtMaster88File _Pc88(ReadOnlySpan<byte> data, ref _Stream stream) {
    if (data[18] == 'B')
      stream.SkipChunk();

    return new() {
      StoredHeight = 200,
      Planes = stream.ReadPlanes(3, 200 * ArtMaster88File.BytesPerRow),
      Palette = [],
    };
  }

  /// <summary>The PC-98 form, whose palette's length says how many planes the picture has.</summary>
  private static ArtMaster88File _Pc98(ReadOnlySpan<byte> data, ref _Stream stream) {
    if (data[17] != 'R' || stream.At + 50 >= data.Length || data[stream.At + 1] != 0)
      throw new InvalidDataException("A 400-line picture has no palette.");

    int length = data[stream.At];
    var planes = length switch {
      50 => 3,
      98 => 4,
      _ => throw new InvalidDataException($"A palette of {length} bytes names no number of planes."),
    };

    if (stream.At + length >= data.Length)
      throw new InvalidDataException("A palette runs past the end of the file.");

    var palette = new byte[16 * 3];
    for (var c = 0; c < 1 << planes; ++c) {
      var at = stream.At + 2 + c * 6;

      // Four bits a channel, each in a word of its own whose high byte is unused — so a colour
      // that sets anything outside those four bits is not a colour this program wrote.
      if ((data[at] & 240) != 0 || (data[at + 2] & 240) != 0 || (data[at + 4] & 240) != 0
          || data[at + 1] != 0 || data[at + 3] != 0 || data[at + 5] != 0)
        throw new InvalidDataException($"Palette entry {c} is not four bits a channel.");

      palette[c * 3] = (byte)(data[at] * 17);
      palette[c * 3 + 1] = (byte)(data[at + 2] * 17);
      palette[c * 3 + 2] = (byte)(data[at + 4] * 17);
    }

    stream.At += length;

    if (data[18] == 'B')
      stream.SkipChunk();

    return new() {
      StoredHeight = 400,
      Planes = stream.ReadPlanes(planes, 400 * ArtMaster88File.BytesPerRow),
      Palette = palette,
    };
  }

  /// <summary>
  /// The stream the planes are packed in, which marks a run by repeating a byte rather than by
  /// spending a value on saying so.
  /// </summary>
  private ref struct _Stream {

    private readonly ReadOnlySpan<byte> _data;
    private int _escape;
    private int _count;
    private int _value;

    public _Stream(ReadOnlySpan<byte> data, int at) {
      this._data = data;
      this.At = at;
      this._escape = -1;
      this._value = -1;
    }

    public int At;

    private int _Byte() => this.At < this._data.Length ? this._data[this.At++] : -1;

    /// <summary>Steps over an optional chunk, which carries its own length in its first word.</summary>
    public void SkipChunk() {
      if (this.At + 1 >= this._data.Length)
        throw new InvalidDataException("A chunk has no length.");

      var length = this._data[this.At] | (this._data[this.At + 1] << 8);
      if (length < 2)
        throw new InvalidDataException("A chunk of no length would never end.");

      this.At += length;
    }

    public byte[][] ReadPlanes(int planes, int planeLength) {
      var result = new byte[planes][];

      for (var plane = 0; plane < planes; ++plane) {
        result[plane] = new byte[planeLength];
        for (var i = 0; i < planeLength; ++i) {
          var value = this._Read();
          if (value < 0)
            throw new InvalidDataException($"Plane {plane} ends before the picture does.");

          result[plane][i] = (byte)value;
        }
      }

      return result;
    }

    private int _Read() {
      while (this._count == 0) {
        var b = this._Byte();
        if (b < 0)
          return -1;

        if (b == this._escape) {
          var extra = this._Byte();
          if (extra < 0)
            return -1;

          // The count covers the repeats beyond the two bytes already written, and wraps, so a
          // count of one adds nothing and the next command follows immediately.
          this._count = (extra - 1) & 255;

          // A run cannot be followed straight away by another, which is what lets a value that
          // happens to repeat across a run boundary stand for itself.
          this._escape = -1;
          continue;
        }

        this._count = 1;
        this._escape = this._value = b;
      }

      --this._count;

      return this._value;
    }
  }

  private static bool _IsStringAt(ReadOnlySpan<byte> data, int offset, string text) {
    if (offset + text.Length > data.Length)
      return false;

    for (var i = 0; i < text.Length; ++i) {
      if (data[offset + i] != text[i])
        return false;
    }

    return true;
  }

  public static ArtMaster88File FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
