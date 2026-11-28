using System;
using System.Collections.Generic;

namespace FileFormat.LuraDocument;

/// <summary>
/// The adaptive binary arithmetic coder of ITU-T T.82 (JBIG), clause 6.8: one probability state and one "more probable
/// symbol" per context, the 113-state estimator of Table 24, and the register conventions of Tables 23 and 25.
/// </summary>
/// <remarks>
/// The estimator table is a constant of the standard and is copied exactly from T.82 Table 24. LuraTech's plug-in carries
/// the same three tables byte for byte (its "next LPS" bytes hold the switch flag in their top bit).
/// </remarks>
internal static class LuraDocumentQmCoder {

  /// <summary>Contexts a stripe can address: ten template pixels give 1024 for the lowest resolution layer.</summary>
  internal const int ContextCount = 1024;

  /// <summary>LPS probability estimate (LSZ) per state.</summary>
  private static readonly ushort[] _Lsz = [
    0x5A1D, 0x2586, 0x1114, 0x080B, 0x03D8, 0x01DA, 0x00E5, 0x006F, 0x0036, 0x001A,
    0x000D, 0x0006, 0x0003, 0x0001, 0x5A7F, 0x3F25, 0x2CF2, 0x207C, 0x17B9, 0x1182,
    0x0CEF, 0x09A1, 0x072F, 0x055C, 0x0406, 0x0303, 0x0240, 0x01B1, 0x0144, 0x00F5,
    0x00B7, 0x008A, 0x0068, 0x004E, 0x003B, 0x002C, 0x5AE1, 0x484C, 0x3A0D, 0x2EF1,
    0x261F, 0x1F33, 0x19A8, 0x1518, 0x1177, 0x0E74, 0x0BFB, 0x09F8, 0x0861, 0x0706,
    0x05CD, 0x04DE, 0x040F, 0x0363, 0x02D4, 0x025C, 0x01F8, 0x01A4, 0x0160, 0x0125,
    0x00F6, 0x00CB, 0x00AB, 0x008F, 0x5B12, 0x4D04, 0x412C, 0x37D8, 0x2FE8, 0x293C,
    0x2379, 0x1EDF, 0x1AA9, 0x174E, 0x1424, 0x119C, 0x0F6B, 0x0D51, 0x0BB6, 0x0A40,
    0x5832, 0x4D1C, 0x438E, 0x3BDD, 0x34EE, 0x2EAE, 0x299A, 0x2516, 0x5570, 0x4CA9,
    0x44D9, 0x3E22, 0x3824, 0x32B4, 0x2E17, 0x56A8, 0x4F46, 0x47E5, 0x41CF, 0x3C3D,
    0x375E, 0x5231, 0x4C0F, 0x4639, 0x415E, 0x5627, 0x50E7, 0x4B85, 0x5597, 0x504F,
    0x5A10, 0x5522, 0x59EB,
  ];

  /// <summary>Next state after coding the more probable symbol (NMPS).</summary>
  private static readonly byte[] _NextMps = [
    1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 13, 15, 16, 17, 18, 19, 20,
    21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 9, 37, 38, 39, 40,
    41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60,
    61, 62, 63, 32, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 48,
    81, 82, 83, 84, 85, 86, 87, 71, 89, 90, 91, 92, 93, 94, 86, 96, 97, 98, 99, 100,
    93, 102, 103, 104, 99, 106, 107, 103, 109, 107, 111, 109, 111,
  ];

  /// <summary>Next state after coding the less probable symbol (NLPS).</summary>
  private static readonly byte[] _NextLps = [
    1, 14, 16, 18, 20, 23, 25, 28, 30, 33, 35, 9, 10, 12, 15, 36, 38, 39, 40, 42,
    43, 45, 46, 48, 49, 51, 52, 54, 56, 57, 59, 60, 62, 63, 32, 33, 37, 64, 65, 67,
    68, 69, 70, 72, 73, 74, 75, 77, 78, 79, 48, 50, 50, 51, 52, 53, 54, 55, 56, 57,
    58, 59, 61, 61, 65, 80, 81, 82, 83, 84, 86, 87, 87, 72, 72, 74, 74, 75, 77, 77,
    80, 88, 89, 90, 91, 92, 93, 86, 88, 95, 96, 97, 99, 99, 93, 95, 101, 102, 103, 104,
    99, 105, 106, 107, 103, 105, 108, 109, 110, 111, 110, 112, 112,
  ];

  /// <summary>Whether coding the less probable symbol in a state swaps the sense of the more probable one (SWTCH).</summary>
  private static bool _Switches(int state) => state is 0 or 14 or 36 or 64 or 80 or 88 or 95 or 105 or 110 or 112;

  /// <summary>The adaptive state of every context: probability state in the low seven bits, the more probable symbol in bit 7.</summary>
  internal sealed class Contexts {
    private readonly byte[] _state = new byte[ContextCount];

    /// <summary>Returns every context to state 0 with a more probable symbol of 0, as at the top of an image.</summary>
    public void Reset() => Array.Clear(this._state);

    internal ref byte this[int context] => ref this._state[context];
  }

  /// <summary>Decodes one stripe's coded data (SCD, already unstuffed) bit by bit: T.82 Figures 31 to 37.</summary>
  internal sealed class Decoder {
    private readonly byte[] _data;
    private int _position;
    private readonly int _end;
    private uint _c;
    private uint _a;
    private int _ct;

    /// <param name="data">Holds the stripe's coded data.</param>
    /// <param name="offset">Where it starts.</param>
    /// <param name="length">How long it is; reads beyond it return zero bytes, as the standard requires.</param>
    public Decoder(byte[] data, int offset, int length) {
      this._data = data;
      this._position = offset;
      this._end = offset + length;
      // INITDEC: three bytes into C, so that its high half holds the first sixteen code bits.
      this._ByteIn();
      this._c <<= 8;
      this._ByteIn();
      this._c <<= 8;
      this._ByteIn();
      this._a = 0x10000;
    }

    private void _ByteIn() {
      var b = this._position < this._end ? this._data[this._position++] : 0;
      this._c += (uint)b << 8;
      this._ct = 8;
    }

    /// <summary>DECODE for one pixel in the given context.</summary>
    public int Decode(Contexts contexts, int context) {
      ref var s = ref contexts[context];
      var state = s & 0x7F;
      var mps = s >> 7;
      var lsz = (uint)_Lsz[state];
      this._a -= lsz;
      int pixel;
      if ((this._c >> 16) < this._a) {
        if (this._a >= 0x8000)
          return mps;

        // MPS_EXCHANGE: the lower sub-interval became the smaller one, so it now stands for the less probable symbol.
        if (this._a < lsz) {
          pixel = 1 - mps;
          s = (byte)(_NextLps[state] | ((_Switches(state) ? 1 - mps : mps) << 7));
        } else {
          pixel = mps;
          s = (byte)(_NextMps[state] | (mps << 7));
        }
      } else {
        // LPS_EXCHANGE: the upper sub-interval of size LSZ.
        this._c -= this._a << 16;
        if (this._a < lsz) {
          pixel = mps;
          s = (byte)(_NextMps[state] | (mps << 7));
        } else {
          pixel = 1 - mps;
          s = (byte)(_NextLps[state] | ((_Switches(state) ? 1 - mps : mps) << 7));
        }

        this._a = lsz;
      }

      // RENORMD
      do {
        if (this._ct == 0)
          this._ByteIn();
        this._a <<= 1;
        this._c <<= 1;
        --this._ct;
      } while (this._a < 0x8000);

      if (this._ct == 0)
        this._ByteIn();
      return pixel;
    }
  }

  /// <summary>Encodes one stripe: T.82 Figures 20 to 30, producing its coded data (SCD, not yet stuffed).</summary>
  internal sealed class Encoder {
    private readonly List<byte> _output = [];
    private uint _c;
    private uint _a = 0x10000;
    private int _ct = 11;
    private int _sc;
    private int _buffer;

    /// <summary>ENCODE one pixel in the given context.</summary>
    public void Encode(Contexts contexts, int context, int pixel) {
      ref var s = ref contexts[context];
      var state = s & 0x7F;
      var mps = s >> 7;
      var lsz = (uint)_Lsz[state];
      this._a -= lsz;
      if (pixel == mps) {
        // CODEMPS
        if (this._a >= 0x8000)
          return;

        if (this._a < lsz) {
          this._c += this._a;
          this._a = lsz;
        }

        s = (byte)(_NextMps[state] | (mps << 7));
      } else {
        // CODELPS
        if (this._a >= lsz) {
          this._c += this._a;
          this._a = lsz;
        }

        s = (byte)(_NextLps[state] | ((_Switches(state) ? 1 - mps : mps) << 7));
      }

      // RENORME
      do {
        this._a <<= 1;
        this._c <<= 1;
        if (--this._ct == 0)
          this._ByteOut();
      } while (this._a < 0x8000);
    }

    private void _ByteOut() {
      var temp = this._c >> 19;
      if (temp > 0xFF) {
        this._output.Add((byte)(this._buffer + 1));
        for (; this._sc > 0; --this._sc)
          this._output.Add(0x00);
        this._buffer = (int)(temp & 0xFF);
      } else if (temp == 0xFF) {
        ++this._sc;
      } else {
        this._output.Add((byte)this._buffer);
        for (; this._sc > 0; --this._sc)
          this._output.Add(0xFF);
        this._buffer = (int)temp;
      }

      this._c &= 0x7FFFF;
      this._ct = 8;
    }

    /// <summary>FLUSH: settles the code register, writes the last bytes and drops the placeholder byte and trailing zeros.</summary>
    public byte[] Finish() {
      // CLEARBITS: the value in [C, C + A) that ends in the most zero bits.
      var temp = (this._c + this._a - 1) & 0xFFFF0000u;
      this._c = temp < this._c ? temp + 0x8000 : temp;

      // FINALWRITES
      this._c <<= this._ct;
      if (this._c > 0x7FFFFFF) {
        this._output.Add((byte)(this._buffer + 1));
        for (; this._sc > 0; --this._sc)
          this._output.Add(0x00);
      } else {
        this._output.Add((byte)this._buffer);
        for (; this._sc > 0; --this._sc)
          this._output.Add(0xFF);
      }

      this._output.Add((byte)((this._c >> 19) & 0xFF));
      this._output.Add((byte)((this._c >> 11) & 0xFF));

      var end = this._output.Count;
      while (end > 1 && this._output[end - 1] == 0)
        --end;
      return this._output.GetRange(1, end - 1).ToArray();
    }
  }
}
