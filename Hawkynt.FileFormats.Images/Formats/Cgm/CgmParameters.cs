using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Cgm;

/// <summary>Reads a command's parameter list at the precisions the file has set.</summary>
/// <remarks>
/// Everything is big-endian. What varies is width: an integer takes as many bits as the file's
/// integer precision, a colour index as many as its colour index precision, a real is either fixed
/// or floating point, and a value in the picture's own coordinates uses a different precision again
/// from an ordinary integer. An enumeration is the exception that catches people out — it is always
/// a signed sixteen-bit value whatever the precisions say.
/// </remarks>
public sealed class CgmParameters(byte[] data, CgmState state) {

  private int _at;

  /// <summary>How many bytes are left unread.</summary>
  public int Remaining => data.Length - this._at;

  /// <summary>Whether anything at all is left.</summary>
  public bool AtEnd => this._at >= data.Length;

  /// <summary>A signed integer at the file's integer precision.</summary>
  public int Integer() => this._Signed(state.IntegerPrecision);

  /// <summary>A signed integer at the file's index precision.</summary>
  public int Index() => this._Signed(state.IndexPrecision);

  /// <summary>An enumeration, which is always sixteen bits whatever else the file has set.</summary>
  public int Enumeration() => this._Signed(16);

  /// <summary>An unsigned value of the given width in bits.</summary>
  public int Unsigned(int bits) {
    var bytes = bits / 8;
    if (this._at + bytes > data.Length)
      throw new InvalidDataException($"A metafile parameter of {bytes} bytes runs past the end of its command.");

    var value = 0;
    for (var i = 0; i < bytes; ++i)
      value = (value << 8) | data[this._at + i];

    this._at += bytes;
    return value;
  }

  /// <summary>A real at the file's real precision, fixed or floating as the file has set.</summary>
  public double Real() => this._Real(state.RealIsFloating, state.RealWhole, state.RealFraction);

  /// <summary>
  /// One coordinate in the picture's own units, which are integers or reals as the file has said.
  /// </summary>
  public double Vdc()
    => state.VdcIsInteger ? this._Signed(state.VdcIntegerPrecision) : this._Real(state.VdcRealIsFloating, state.VdcRealWhole, state.VdcRealFraction);

  /// <summary>How many bytes one coordinate takes, which is what turns a list's length into a count.</summary>
  public int VdcSize
    => state.VdcIsInteger
      ? state.VdcIntegerPrecision / 8
      : state.VdcRealIsFloating ? (state.VdcRealWhole + state.VdcRealFraction + 1) / 8 : (state.VdcRealWhole + state.VdcRealFraction) / 8;

  /// <summary>A point, x then y.</summary>
  public (double X, double Y) Point() => (this.Vdc(), this.Vdc());

  /// <summary>
  /// A colour, which is an index into the table or three components, as the file has said.
  /// </summary>
  public Rgba32 Colour() {
    if (!state.DirectColour)
      return state.Lookup(this.Unsigned(state.ColourIndexPrecision));

    var bits = state.ColourPrecision;
    return new(state.Component(this.Unsigned(bits), 0), state.Component(this.Unsigned(bits), 1), state.Component(this.Unsigned(bits), 2));
  }

  /// <summary>Three components read as a colour whether or not the file is in direct mode.</summary>
  public Rgba32 DirectColour() {
    var bits = state.ColourPrecision;
    return new(state.Component(this.Unsigned(bits), 0), state.Component(this.Unsigned(bits), 1), state.Component(this.Unsigned(bits), 2));
  }

  /// <summary>
  /// A string: one length byte, or 255 and then a sixteen-bit length that may continue.
  /// </summary>
  public string Text() {
    if (this.AtEnd)
      return string.Empty;

    var builder = new StringBuilder();
    var length = (int)data[this._at++];

    if (length < 255) {
      length = Math.Min(length, this.Remaining);
      builder.Append(Encoding.Latin1.GetString(data, this._at, length));
      this._at += length;
      return builder.ToString();
    }

    while (this._at + 2 <= data.Length) {
      var word = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(this._at));
      this._at += 2;

      var more = (word & 0x8000) != 0;
      var part = Math.Min(word & 0x7FFF, this.Remaining);
      builder.Append(Encoding.Latin1.GetString(data, this._at, part));
      this._at += part;

      if (!more)
        break;
    }

    return builder.ToString();
  }

  /// <summary>Steps over the rest of the parameter list.</summary>
  public void Skip() => this._at = data.Length;

  private int _Signed(int bits) {
    var value = this.Unsigned(bits);
    var sign = 1 << (bits - 1);
    return (value & sign) != 0 ? value - (sign << 1) : value;
  }

  private double _Real(bool floating, int whole, int fraction) {
    if (floating)
      return whole + fraction == 31
        ? BitConverter.Int32BitsToSingle(this._Signed(32))
        : BitConverter.Int64BitsToDouble(_ToLong(this._Signed(32), this.Unsigned(32)));

    // Fixed point: a signed whole part and an unsigned fraction, so a negative value is the floor
    // plus a positive fraction and the two add without either being negated.
    var integer = this._Signed(whole);
    var scale = this.Unsigned(fraction);
    return integer + scale / Math.Pow(2, fraction);
  }

  private static long _ToLong(int high, int low) => ((long)high << 32) | (uint)low;
}
