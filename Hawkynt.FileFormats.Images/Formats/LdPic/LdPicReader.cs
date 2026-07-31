using System;
using System.IO;

namespace FileFormat.LdPic;

/// <summary>Reads LdPic pictures from bytes, streams, or file paths.</summary>
public static class LdPicReader {

  public static LdPicFile FromFile(FileInfo file) {
    ArgumentNullException.ThrowIfNull(file);
    if (!file.Exists)
      throw new FileNotFoundException("Picture not found.", file.FullName);

    return FromBytes(File.ReadAllBytes(file.FullName));
  }

  public static LdPicFile FromStream(Stream stream) {
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

  public static LdPicFile FromSpan(ReadOnlySpan<byte> data) {
    var stream = new _Stream(data);

    var valueBits = stream.Bits(8);
    if (valueBits is < 1 or > 8)
      throw new InvalidDataException("Not an LdPic picture: a value is not one to eight bits.");

    var mode = stream.Bits(8);
    var size = mode switch {
      0 or 1 or 2 => 20480,
      4 or 5 => 10240,
      _ => throw new InvalidDataException($"Screen mode {mode} is not one the BBC Micro has."),
    };

    // Sixteen logical colours, read from the last backwards, each naming one of the machine's own.
    var colors = new byte[16 * 3];
    for (var i = 15; i >= 0; --i) {
      var c = stream.Bits(4);
      if (c < 0)
        throw new InvalidDataException("An LdPic picture ends inside its palette.");

      var color = LdPicFile.Palette[c];
      colors[i * 3] = (byte)(color >> 16);
      colors[i * 3 + 1] = (byte)(color >> 8);
      colors[i * 3 + 2] = (byte)color;
    }

    // How far apart the bytes the unpacker visits in turn are. Bytes eight scanlines apart are
    // adjacent in the machine's cell layout, so interleaving turns a flat area into one run.
    var step = stream.Bits(8);
    if (step <= 0)
      throw new InvalidDataException("An LdPic picture interleaves by no step at all.");

    var countBits = stream.Bits(8);
    if (countBits is < 1 or > 8)
      throw new InvalidDataException("A run length is not one to eight bits.");

    var screen = new byte[size];
    for (var column = step - 1; column >= 0; --column) {
      for (var at = column; at < size; at += step) {
        var value = stream.Read(valueBits, countBits);
        if (value < 0)
          throw new InvalidDataException("An LdPic picture ends before its screen does.");

        screen[at] = (byte)value;
      }
    }

    return new() { Screen = screen, Mode = mode, LogicalColors = colors };
  }

  /// <summary>
  /// The bit stream the whole file is, taking bits from the top of each byte but assembling them
  /// into a value from the bottom up — so a field reads backwards relative to how it is stored.
  /// </summary>
  private ref struct _Stream {

    private readonly ReadOnlySpan<byte> _data;
    private int _at;
    private int _bits;
    private int _count;
    private int _value;

    public _Stream(ReadOnlySpan<byte> data) {
      this._data = data;
    }

    private int _Bit() {
      if ((this._bits & 127) == 0) {
        if (this._at >= this._data.Length)
          return -1;

        this._bits = (this._data[this._at++] << 1) | 1;
      } else
        this._bits <<= 1;

      return (this._bits >> 8) & 1;
    }

    public int Bits(int count) {
      var result = 0;
      for (var i = 0; i < count; ++i) {
        var bit = this._Bit();
        if (bit < 0)
          return -1;

        result |= bit << i;
      }

      return result;
    }

    /// <summary>
    /// Reads one screen byte. A leading zero bit means the value stands alone; a one means a run
    /// length precedes it.
    /// </summary>
    public int Read(int valueBits, int countBits) {
      while (this._count == 0) {
        var marked = this._Bit();
        if (marked < 0)
          return -1;

        if (marked == 0)
          this._count = 1;
        else {
          this._count = this.Bits(countBits);
          if (this._count <= 0)
            return -1;
        }

        this._value = this.Bits(valueBits);
        if (this._value < 0)
          return -1;
      }

      --this._count;

      return this._value;
    }
  }

  public static LdPicFile FromBytes(byte[] data) {
    ArgumentNullException.ThrowIfNull(data);
    return FromSpan(data);
  }
}
