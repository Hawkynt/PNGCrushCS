using System;

namespace FileFormat.Codecs.G2m;

/// <summary>
/// The ELS ("entropy logarithmic scale") binary arithmetic decoder Go2Meeting's ePIC coder runs on:
/// FFmpeg's <c>libavcodec/elsdec.c</c> (LGPL-2.1-or-later), with its ladder of rungs as
/// <see cref="G2mTables"/>.
/// </summary>
/// <remarks>
/// A rung is one byte of state: an index into the ladder, whose entry says how many jots each outcome
/// costs and which rung follows it. The decoder keeps FFmpeg's error convention — once the data runs
/// out every later bit is nought, and the bit that ran out comes back negative — because the ePIC coder
/// above treats a negative bit as a one in places and that is part of what it decodes.
/// </remarks>
internal sealed class ElsDecoder {

  private const int _JOTS_PER_BYTE = 36;
  private const int _MAX = 1 << 24;
  private const int _ALLOWABLE = _JOTS_PER_BYTE * 3;

  /// <summary>The longest exp-Golomb prefix an unsigned number may have.</summary>
  internal const int ExpGolombLength = 10;

  private readonly byte[] _data;
  private int _position;
  private int _remaining;
  private uint _x;
  private int _j;
  private int _t;
  private int _difference;

  internal ElsDecoder(byte[] data, int offset, int size) {
    this._data = data;
    int count;
    if (size >= 3) {
      this._x = (uint)(data[offset] << 16 | data[offset + 1] << 8 | data[offset + 2]);
      count = 3;
    } else if (size == 2) {
      this._x = (uint)(data[offset] << 8 | data[offset + 1]);
      count = 2;
    } else {
      this._x = data[offset];
      count = 1;
    }

    this._position = offset + count;
    this._remaining = size - count;
    this._j = _JOTS_PER_BYTE;
    this._t = _MAX;
    // Unsigned arithmetic, as C does it with an unsigned x.
    this._difference = unchecked((int)Math.Min((uint)_MAX - this._x, (uint)_MAX - G2mTables.ExpTable[_JOTS_PER_BYTE * 4 - 1]));
  }

  /// <summary>Whether the data ran out or an unsigned number overflowed: FFmpeg's <c>err</c>.</summary>
  internal bool Failed { get; private set; }

  private static uint _Allowable(int index) => G2mTables.ExpTable[_ALLOWABLE + index];

  private bool _ImportByte() {
    if (this._remaining == 0) {
      this.Failed = true;
      return false;
    }

    this._x = (this._x << 8) | this._data[this._position++];
    --this._remaining;
    this._j += _JOTS_PER_BYTE;
    this._t <<= 8;
    return true;
  }

  /// <summary><c>ff_els_decode_bit</c>: nought or one, or negative where the data ran out on this bit.</summary>
  internal int DecodeBit(ref byte rung) {
    if (this.Failed)
      return 0;

    var z = unchecked((int)_Allowable(this._j + G2mTables.LadderLps[rung]));
    this._t -= z;
    this._difference -= z;
    if (this._difference > 0)
      return rung & 1;

    int bit;
    if (unchecked((uint)this._t) > this._x) {
      this._j += G2mTables.LadderMps[rung];
      while (unchecked((uint)this._t) > _Allowable(this._j))
        ++this._j;

      if (this._j <= 0 && !this._ImportByte())
        return -1;

      z = this._t;
      bit = rung & 1;
      rung = G2mTables.LadderNext0[rung];
    } else {
      this._x = unchecked(this._x - (uint)this._t);
      this._t = z;
      this._j += G2mTables.LadderLps[rung];
      if (this._j <= 0) {
        z <<= 8;
        if (!this._ImportByte())
          return -1;
        if (this._j <= 0) {
          z <<= 8;
          if (!this._ImportByte())
            return -1;
          while (_Allowable(this._j - 1) >= unchecked((uint)z))
            --this._j;
        }
      }

      bit = (rung & 1) ^ 1;
      rung = G2mTables.LadderNext1[rung];
    }

    this._difference = unchecked((int)Math.Min((uint)z - this._x, (uint)z - _Allowable(this._j - 1)));
    return bit;
  }

  /// <summary><c>ff_els_decode_unsigned</c>: an exp-Golomb number whose bits each have a rung of their own.</summary>
  internal uint DecodeUnsigned(UnsignedRung rung) {
    if (this.Failed)
      return 0;

    int n;
    for (n = 0; n < ExpGolombLength + 1; ++n)
      if (this.DecodeBit(ref rung.Prefix[n]) != 0)
        break;

    if (this.Failed || n >= ExpGolombLength) {
      this.Failed = true;
      return 0;
    }

    if (n == 0)
      return 0;

    rung.Initialise();
    var node = 0;
    var r = 0;
    var bit = 0;
    for (var i = 0; i < n; ++i) {
      if (i == 0) {
        node = n;
      } else {
        if (rung.NextIndex(node) == 0)
          rung.Allocate(node);
        node = rung.NextIndex(node) + bit;
      }

      bit = this.DecodeBit(ref rung.RungOf(node));
      if (this.Failed)
        return (uint)bit;
      r = (r << 1) + bit;
    }

    return (uint)((1 << n) - 1 + r);
  }

  /// <summary><c>ElsUnsignedRung</c>: the prefix rungs and the tree of remainder rungs, grown on demand.</summary>
  internal sealed class UnsignedRung {

    private const int _SPACE = 64;

    internal readonly byte[] Prefix = new byte[ExpGolombLength + 1];
    private byte[] _rungs = [];
    private ushort[] _next = [];
    private ushort _available;

    internal void Initialise() {
      if (this._rungs.Length != 0)
        return;
      this._rungs = new byte[_SPACE];
      this._next = new ushort[_SPACE];
      this._available = ExpGolombLength;
    }

    internal int NextIndex(int node) => this._next[node];

    internal ref byte RungOf(int node) => ref this._rungs[node];

    internal void Allocate(int node) {
      if (this._rungs.Length <= this._available + 2) {
        Array.Resize(ref this._rungs, this._rungs.Length + _SPACE);
        Array.Resize(ref this._next, this._next.Length + _SPACE);
      }

      this._next[node] = this._available;
      this._available += 2;
    }
  }
}
