using System;
using System.Collections.Generic;
using System.IO;

namespace FileFormat.Iwc;

/// <summary>The entropy coder of an IWC payload: two adaptive semi-splay prefix-code trees over one bit stream.</summary>
/// <remarks>
/// <para>
/// This is Douglas W. Jones's splay-tree prefix code ("Application of Splay Trees to Data
/// Compression", CACM 31(8), 1988): a code tree that starts complete and balanced, and after every
/// coded value is semi-splayed so that the value's leaf moves halfway towards the root. IWC keeps
/// two such trees sharing root number 1 — one over the 253 sample values 0..252, one over the run
/// lengths 0..3000 — and one bit stream read least significant bit first.
/// </para>
/// <para>
/// The stream is a sequence of items. A 1 bit is followed by one sample value. A 0 bit is followed
/// by a run length and then a sample value, which stands for that many copies of it.
/// </para>
/// </remarks>
internal static class IwcSplayCoder {

  /// <summary>Largest sample value the symbol tree can carry.</summary>
  public const int MaxSymbol = 252;

  /// <summary>Largest run the run tree can carry.</summary>
  public const int MaxRun = 3000;

  private const int _ROOT = 1;

  /// <summary>One semi-splay tree with leaves for the values 0..<c>maxValue</c>.</summary>
  private sealed class Tree {
    private readonly int _maxInternal;
    private readonly short[] _left;
    private readonly short[] _right;
    private readonly short[] _up;
    private readonly bool[] _path;

    public Tree(int maxValue) {
      this._path = new bool[maxValue + 1];
      this._maxInternal = maxValue;
      this._left = new short[maxValue + 1];
      this._right = new short[maxValue + 1];
      this._up = new short[2 * maxValue + 2];
      for (var node = 2; node < this._up.Length; ++node)
        this._up[node] = (short)(node / 2);
      for (var node = 1; node <= maxValue; ++node) {
        this._left[node] = (short)(2 * node);
        this._right[node] = (short)(2 * node + 1);
      }
    }

    /// <summary>Walks from the root to a leaf, reading one bit per branch, and splays the leaf.</summary>
    public int Decode(BitReader bits) {
      int node = _ROOT;
      do
        node = bits.Read() ? this._right[node] : this._left[node];
      while (node <= this._maxInternal);

      var value = node - this._maxInternal - 1;
      this._Splay(value);
      return value;
    }

    /// <summary>Writes the branch bits from the root to the value's leaf, and splays the leaf.</summary>
    public void Encode(int value, BitWriter bits) {
      var path = this._path;
      var length = 0;
      for (int node = value + this._maxInternal + 1; node != _ROOT; node = this._up[node])
        path[length++] = this._right[this._up[node]] == node;

      while (length > 0)
        bits.Write(path[--length]);

      this._Splay(value);
    }

    /// <summary>Jones's semi-splay: each step swaps the leaf's subtree with its grandparent's other child.</summary>
    private void _Splay(int value) {
      int a = value + this._maxInternal + 1;
      do {
        int c = this._up[a];
        if (c == _ROOT) {
          a = c;
          continue;
        }

        int d = this._up[c];
        int b = this._left[d];
        if (c == b) {
          b = this._right[d];
          this._right[d] = (short)a;
        } else
          this._left[d] = (short)a;

        if (this._left[c] == a)
          this._left[c] = (short)b;
        else
          this._right[c] = (short)b;

        this._up[a] = (short)d;
        this._up[b] = (short)c;
        a = d;
      } while (a != _ROOT);
    }
  }

  /// <summary>Expands <paramref name="symbolCount"/> sample values out of the payload.</summary>
  public static byte[] Decode(ReadOnlySpan<byte> payload, int symbolCount) {
    var bits = new BitReader(payload.ToArray());
    var values = new Tree(MaxSymbol);
    var runs = new Tree(MaxRun);
    var output = new byte[symbolCount];
    var written = 0;
    while (written < symbolCount) {
      if (bits.Read()) {
        output[written++] = (byte)values.Decode(bits);
        continue;
      }

      var run = runs.Decode(bits);
      var value = (byte)values.Decode(bits);
      if (run > symbolCount - written)
        throw new InvalidDataException($"IWC run of {run} overruns the {symbolCount} coefficients the header calls for.");

      output.AsSpan(written, run).Fill(value);
      written += run;
    }

    return output;
  }

  /// <summary>Codes the sample values, using runs for repeats of <c>minimumRun</c> or more.</summary>
  public static byte[] Encode(ReadOnlySpan<byte> symbols, int minimumRun = 3) {
    var bits = new BitWriter();
    var values = new Tree(MaxSymbol);
    var runs = new Tree(MaxRun);
    for (var i = 0; i < symbols.Length;) {
      var value = symbols[i];
      if (value > MaxSymbol)
        throw new ArgumentOutOfRangeException(nameof(symbols), $"IWC sample values stop at {MaxSymbol}.");

      var run = 1;
      while (i + run < symbols.Length && symbols[i + run] == value && run < MaxRun)
        ++run;

      if (run < minimumRun) {
        bits.Write(true);
        values.Encode(value, bits);
        ++i;
        continue;
      }

      bits.Write(false);
      runs.Encode(run, bits);
      values.Encode(value, bits);
      i += run;
    }

    return bits.ToArray();
  }

  /// <summary>Bits least significant first, as the applet's <c>receive</c> takes them.</summary>
  internal sealed class BitReader(byte[] data) {
    private int _position;

    public bool Read() {
      var index = this._position >> 3;
      if (index >= data.Length)
        throw new InvalidDataException("IWC payload ends before every coefficient has been decoded.");

      var bit = (data[index] >> (this._position & 7) & 1) != 0;
      ++this._position;
      return bit;
    }
  }

  internal sealed class BitWriter {
    private readonly List<byte> _bytes = [];
    private int _count;

    public void Write(bool bit) {
      if ((this._count & 7) == 0)
        this._bytes.Add(0);
      if (bit)
        this._bytes[^1] |= (byte)(1 << (this._count & 7));
      ++this._count;
    }

    public byte[] ToArray() => [.. this._bytes];
  }
}
