using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace FileFormat.Cgm;

/// <summary>
/// Builds the parameters of a binary-encoded element (ISO/IEC 8632-3), for the readers that
/// translate the other two encodings into the binary one.
/// </summary>
internal sealed class CgmCommandWriter {
  private readonly List<byte> _bytes = [];

  internal byte[] ToArray() => [.. this._bytes];

  internal void Int32(long value) {
    Span<byte> b = stackalloc byte[4];
    BinaryPrimitives.WriteInt32BigEndian(b, checked((int)value));
    this._bytes.AddRange(b.ToArray());
  }

  internal void UInt(long value, int bits) {
    for (var shift = bits - 8; shift >= 0; shift -= 8)
      this._bytes.Add((byte)(value >> shift));
  }

  internal void Enum(int value) {
    this._bytes.Add((byte)(value >> 8));
    this._bytes.Add((byte)value);
  }

  internal void Double(double value) {
    Span<byte> b = stackalloc byte[8];
    BinaryPrimitives.WriteDoubleBigEndian(b, value);
    this._bytes.AddRange(b.ToArray());
  }

  internal void String(string value) {
    var bytes = Encoding.Latin1.GetBytes(value);
    if (bytes.Length < 255) {
      this._bytes.Add((byte)bytes.Length);
    } else {
      this._bytes.Add(255);
      this._bytes.Add((byte)(bytes.Length >> 8));
      this._bytes.Add((byte)bytes.Length);
    }

    this._bytes.AddRange(bytes);
  }

  /// <summary>Pads to the 16-bit word a cell array row starts on.</summary>
  internal void Align() {
    if ((this._bytes.Count & 1) != 0)
      this._bytes.Add(0);
  }
}

internal static class CgmTranslation {

  /// <summary>The smallest binary colour precision holding the value: 8, 16, 24 or 32 bits.</summary>
  internal static int BitsFor(long maximum) {
    var bits = 8;
    while (bits < 32 && maximum > (1L << bits) - 1)
      bits += 8;
    return bits;
  }

  /// <summary>
  /// The precisions every translation writes once after BEGIN METAFILE: 32-bit integers, indices,
  /// colour indices and integer coordinates, 64-bit floating reals and real coordinates — wide
  /// enough that no value either of the other encodings can state is changed on the way.
  /// </summary>
  internal static void Precisions(List<CgmCommand> commands) {
    void Int(int elementClass, int elementId, int value) {
      var w = new CgmCommandWriter();
      w.Int32(value);
      commands.Add(new(elementClass, elementId, w.ToArray()));
    }

    // Integer precision is stated at the default sixteen bits first, since it is in that
    // precision that the following precision elements are themselves read.
    var integer = new CgmCommandWriter();
    integer.UInt(32, 16);
    commands.Add(new(1, 4, integer.ToArray()));
    Int(1, 6, 32);
    Int(1, 8, 32);
    var real = new CgmCommandWriter();
    real.Enum(0);
    real.Int32(12);
    real.Int32(52);
    commands.Add(new(1, 5, real.ToArray()));
    Int(3, 1, 32);
    var vdcReal = new CgmCommandWriter();
    vdcReal.Enum(0);
    vdcReal.Int32(12);
    vdcReal.Int32(52);
    commands.Add(new(3, 2, vdcReal.ToArray()));
  }
}
