using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Wic;

/// <summary>The binary arithmetic coder every JWIC plane is written with.</summary>
/// <remarks>
/// It is the QM coder of JBIG (ITU-T T.82): a 16-bit interval register, a probability-estimation
/// state per context and the T.82 transition tables. Two details are JWIC's own and decide whether
/// a stream is read correctly at all: a <c>0xFF</c> byte is always followed by a stuffed <c>0x00</c>,
/// and the stream is closed by the marker <c>FF 02</c>, after which every further read yields zero.
/// The decoder primes its register with three bytes rather than the two T.82 describes.
/// </remarks>
internal static class WicQmCoder {

  /// <summary>Number of coding contexts a JWIC stream uses.</summary>
  public const int ContextCount = 132;

  /// <summary>Context of a sign bit.</summary>
  public const int SignContext = 0;

  /// <summary>Context of the "keep scanning" bit coded before every node.</summary>
  public const int ContinueContext = 1;

  /// <summary>Context of a refinement bit of an already significant coefficient.</summary>
  public const int RefinementContext = 2;

  /// <summary>First context of a node's own significance, indexed by pyramid level (+16 when the parent just became significant).</summary>
  public const int NodeContext = 3;

  /// <summary>First context of a horizontal-band coefficient's significance.</summary>
  public const int HorizontalContext = 35;

  /// <summary>First context of a diagonal-band coefficient's significance.</summary>
  public const int DiagonalContext = 67;

  /// <summary>First context of a vertical-band coefficient's significance.</summary>
  public const int VerticalContext = 99;

  /// <summary>Context of a low-pass (top of the pyramid) coefficient's significance.</summary>
  public const int LowPassContext = 131;

  private const int _ESCAPE = 0xFF;
  private const int _STUFF = 0x00;
  private const int _END_OF_STREAM = 0x02;

  // ITU-T T.82 Table 24, copied verbatim: probability estimate (Qe), next state after an MPS, next
  // state after an LPS, and whether an LPS swaps the sense of the MPS.
  private static readonly ushort[] _Qe = [
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

  private static readonly byte[] _NextMps = [
    1, 2, 3, 4, 5, 6, 7, 8, 9, 10,
    11, 12, 13, 13, 15, 16, 17, 18, 19, 20,
    21, 22, 23, 24, 25, 26, 27, 28, 29, 30,
    31, 32, 33, 34, 35, 9, 37, 38, 39, 40,
    41, 42, 43, 44, 45, 46, 47, 48, 49, 50,
    51, 52, 53, 54, 55, 56, 57, 58, 59, 60,
    61, 62, 63, 32, 65, 66, 67, 68, 69, 70,
    71, 72, 73, 74, 75, 76, 77, 78, 79, 48,
    81, 82, 83, 84, 85, 86, 87, 71, 89, 90,
    91, 92, 93, 94, 86, 96, 97, 98, 99, 100,
    93, 102, 103, 104, 99, 106, 107, 103, 109, 107,
    111, 109, 111,
  ];

  private static readonly byte[] _NextLps = [
    1, 14, 16, 18, 20, 23, 25, 28, 30, 33,
    35, 9, 10, 12, 15, 36, 38, 39, 40, 42,
    43, 45, 46, 48, 49, 51, 52, 54, 56, 57,
    59, 60, 62, 63, 32, 33, 37, 64, 65, 67,
    68, 69, 70, 72, 73, 74, 75, 77, 78, 79,
    48, 50, 50, 51, 52, 53, 54, 55, 56, 57,
    58, 59, 61, 61, 65, 80, 81, 82, 83, 84,
    86, 87, 87, 72, 72, 74, 74, 75, 77, 77,
    80, 88, 89, 90, 91, 92, 93, 86, 88, 95,
    96, 97, 99, 99, 93, 95, 101, 102, 103, 104,
    99, 105, 106, 107, 103, 105, 108, 109, 110, 111,
    110, 112, 112,
  ];

  private static readonly bool[] _SwitchOnLps = [
    true, false, false, false, false, false, false, false, false, false,
    false, false, false, false, true, false, false, false, false, false,
    false, false, false, false, false, false, false, false, false, false,
    false, false, false, false, false, false, true, false, false, false,
    false, false, false, false, false, false, false, false, false, false,
    false, false, false, false, false, false, false, false, false, false,
    false, false, false, false, true, false, false, false, false, false,
    false, false, false, false, false, false, false, false, false, false,
    true, false, false, false, false, false, false, false, true, false,
    false, false, false, false, false, true, false, false, false, false,
    false, false, false, false, false, true, false, false, false, false,
    true, false, true,
  ];

  /// <summary>Reads bits out of a JWIC coder stream.</summary>
  internal sealed class Decoder {
    private readonly byte[] _data;
    private int _position;
    private bool _ended;
    private uint _c;
    private int _a;
    private int _ct;
    private readonly byte[] _state = new byte[ContextCount];
    private readonly byte[] _mps = new byte[ContextCount];

    /// <summary>Starts decoding at <paramref name="offset"/>.</summary>
    public Decoder(byte[] data, int offset) {
      this._data = data;
      this._position = offset;
      this._c = (uint)this._NextByte() << 24;
      this._c |= (uint)this._NextByte() << 16;
      this._c |= (uint)this._NextByte() << 8;
      this._a = 0x10000;
      this._ct = 8;
    }

    /// <summary>Whether the end-of-stream marker has been read.</summary>
    public bool ReachedEnd => this._ended;

    private int _NextByte() {
      if (this._ended)
        return 0;

      if (this._position >= this._data.Length)
        throw new InvalidDataException("JWIC coder stream is truncated: it ends before its FF 02 terminator.");

      int value = this._data[this._position++];
      if (value != _ESCAPE)
        return value;

      if (this._position >= this._data.Length)
        throw new InvalidDataException("JWIC coder stream is truncated inside an escape sequence.");

      switch (this._data[this._position++]) {
        case _STUFF:
          return _ESCAPE;
        case _END_OF_STREAM:
          this._ended = true;
          return 0;
        default:
          throw new InvalidDataException($"JWIC coder stream holds the unknown escape code FF {this._data[this._position - 1]:X2}.");
      }
    }

    /// <summary>Decodes one bit in <paramref name="context"/>.</summary>
    public int Decode(int context) {
      ref var state = ref this._state[context];
      ref var mps = ref this._mps[context];
      int qe = _Qe[state];
      this._a = (this._a - qe) & 0xFFFF;

      int bit;
      if ((this._c >> 16) < (uint)this._a) {
        if (this._a >= 0x8000)
          return mps;

        // The MPS sub-interval has shrunk below half: renormalise, exchanging if it is now the smaller one.
        if (this._a < qe) {
          bit = 1 - mps;
          if (_SwitchOnLps[state])
            mps = (byte)(1 - mps);
          state = _NextLps[state];
        } else {
          bit = mps;
          state = _NextMps[state];
        }
      } else {
        this._c -= (uint)this._a << 16;
        if (this._a < qe) {
          bit = mps;
          state = _NextMps[state];
        } else {
          bit = 1 - mps;
          if (_SwitchOnLps[state])
            mps = (byte)(1 - mps);
          state = _NextLps[state];
        }

        this._a = qe;
      }

      do {
        if (this._ct == 0) {
          this._c += (uint)this._NextByte() << 8;
          this._ct = 8;
        }

        this._a = (this._a << 1) & 0xFFFF;
        this._c <<= 1;
        --this._ct;
      } while (this._a < 0x8000);

      return bit;
    }
  }

  /// <summary>Writes bits into a JWIC coder stream, counting bytes the way the budget check expects.</summary>
  internal sealed class Encoder {
    private readonly List<byte> _output;
    private uint _c;
    private int _a = 0x10000;
    private int _ct = 11;
    private int _buffer;
    private int _pendingZeros;
    private int _pendingFfs;
    private bool _suppressFirst = true;
    private readonly byte[] _state = new byte[ContextCount];
    private readonly byte[] _mps = new byte[ContextCount];

    /// <summary>Appends the coded stream to <paramref name="output"/>.</summary>
    public Encoder(List<byte> output) => this._output = output;

    /// <summary>Coded bytes emitted so far, stuffing included, trailing zero bytes not yet committed excluded.</summary>
    public long BytesWritten { get; private set; }

    /// <summary>Codes <paramref name="bit"/> in <paramref name="context"/>.</summary>
    public void Encode(int bit, int context) {
      ref var state = ref this._state[context];
      ref var mps = ref this._mps[context];
      int qe = _Qe[state];
      this._a = (this._a - qe) & 0xFFFF;

      if (bit == mps) {
        if (this._a >= 0x8000)
          return;

        if (this._a < qe) {
          this._c += (uint)this._a;
          this._a = qe;
        }

        state = _NextMps[state];
      } else {
        if (this._a >= qe) {
          this._c += (uint)this._a;
          this._a = qe;
        }

        if (_SwitchOnLps[state])
          mps = (byte)(1 - mps);
        state = _NextLps[state];
      }

      while (this._a < 0x8000) {
        this._a <<= 1;
        this._c <<= 1;
        if (--this._ct == 0)
          this._ShiftOutByte();
      }
    }

    /// <summary>Flushes the register and appends the <c>FF 02</c> terminator.</summary>
    public void Finish() {
      // The interval register is 16 bits wide: before the first symbol it holds 0, not 0x10000.
      var rounded = (this._c + (uint)(this._a & 0xFFFF) - 1) & 0xFFFF0000u;
      this._c = rounded < this._c ? rounded + 0x8000u : rounded;
      this._c <<= this._ct;
      if (this._c > 0x7FFFFFFu) {
        this._Commit(this._buffer + 1);
        for (; this._pendingFfs > 0; --this._pendingFfs)
          this._CommitZero();
      } else {
        this._Commit(this._buffer);
        for (; this._pendingFfs > 0; --this._pendingFfs)
          this._CommitFf();
      }

      this._Commit((int)((this._c >> 19) & 0xFF));
      this._Commit((int)((this._c >> 11) & 0xFF));

      // Zero bytes still pending are dropped on purpose: the decoder reads zeros past the terminator.
      this._output.Add(_ESCAPE);
      this._output.Add(_END_OF_STREAM);
    }

    private void _ShiftOutByte() {
      var top = (int)((this._c >> 19) & 0x1FF);
      if (top > 0xFF) {
        // A carry reached the buffered byte, so every 0xFF held back behind it rolls over to zero.
        this._Commit(this._buffer + 1);
        for (; this._pendingFfs > 0; --this._pendingFfs)
          this._CommitZero();
        this._buffer = top & 0xFF;
      } else if (top == 0xFF)
        ++this._pendingFfs;
      else {
        this._Commit(this._buffer);
        for (; this._pendingFfs > 0; --this._pendingFfs)
          this._CommitFf();
        this._buffer = top;
      }

      this._c &= 0x7FFFF;
      this._ct = 8;
    }

    private void _Commit(int value) {
      if (this._SwallowFirst())
        return;

      if (value == 0) {
        ++this._pendingZeros;
        return;
      }

      this._FlushZeros();
      this._Emit((byte)value);
    }

    private void _CommitZero() {
      if (!this._SwallowFirst())
        ++this._pendingZeros;
    }

    private void _CommitFf() {
      if (this._SwallowFirst())
        return;

      this._FlushZeros();
      this._Emit(0xFF);
    }

    /// <summary>The very first byte the register produces is a placeholder and never reaches the stream.</summary>
    private bool _SwallowFirst() {
      if (!this._suppressFirst)
        return false;

      this._suppressFirst = false;
      return true;
    }

    private void _FlushZeros() {
      for (; this._pendingZeros > 0; --this._pendingZeros)
        this._Emit(0);
    }

    private void _Emit(byte value) {
      ++this.BytesWritten;
      this._output.Add(value);
      if (value != _ESCAPE)
        return;

      ++this.BytesWritten;
      this._output.Add(_STUFF);
    }
  }
}
