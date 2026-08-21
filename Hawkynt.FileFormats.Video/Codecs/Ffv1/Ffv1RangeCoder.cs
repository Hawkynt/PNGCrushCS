using System;
using System.IO;

namespace FileFormat.Codecs.Ffv1;

/// <summary>
/// FFV1's binary range coder, and the coding of whole numbers on top of it (RFC 9043 §3.8.1).
/// </summary>
/// <remarks>
/// A range coder rather than an arithmetic coder in the usual sense: there is no carry propagation
/// and no explicit probability, only a state per context that indexes a transition table. Reading a
/// bit splits the current range in proportion to the state, and the state then moves along whichever
/// of the two tables the bit chose. That is the whole of the entropy coding — everything else in
/// FFV1 is prediction and context modelling.
/// <para/>
/// Whole numbers are coded as an exponent in unary, then a mantissa, then a sign, each bit with its
/// own state out of the context's thirty-two. The layout of those thirty-two is fixed by the
/// specification and is what makes a context worth having: the bit that says "this difference is
/// zero" learns separately from the bit that says how big it is if it is not.
/// </remarks>
internal sealed class Ffv1RangeCoder {

  /// <summary>How many states one context owns, which the specification names <c>CONTEXT_SIZE</c>.</summary>
  internal const int CONTEXT_SIZE = 32;

  private readonly ReadOnlyMemory<byte> _data;
  private byte[] _zeroState;
  private byte[] _oneState;
  private int _position;
  private int _range;
  private int _low;
  private bool _end;

  internal Ffv1RangeCoder(ReadOnlyMemory<byte> data, byte[] zeroState, byte[] oneState) {
    this._data = data;
    this._zeroState = zeroState;
    this._oneState = oneState;

    this._range = 0xFF00;
    this._low = (this._Next() << 8) | this._Next();
    if (this._low < this._range)
      return;

    this._low = this._range;
    this._end = true;
  }

  /// <summary>How many bytes of the input the coder has taken, which is where what follows begins.</summary>
  internal int BytesRead => this._position;

  /// <summary>
  /// Puts a stream's own state transition tables in place, part way through reading it.
  /// </summary>
  /// <remarks>
  /// A stream that states its own tables states them as differences from the default one, coded with
  /// the default one — so the coder reads the header with the tables it was built with and changes
  /// to the stated ones for everything after. Building a second coder instead would lose the range
  /// and the bytes already read, which are the whole of its position in the stream.
  /// </remarks>
  internal void UseStateTransitions(byte[] zeroState, byte[] oneState) {
    this._zeroState = zeroState;
    this._oneState = oneState;
  }

  private byte _Next() => this._position < this._data.Length ? this._data.Span[this._position++] : (byte)0;

  /// <summary>Reads one bit against a state, and moves that state along.</summary>
  internal int Get(byte[] states, int index) {
    var state = states[index];
    var split = this._range * state >> 8;
    this._range -= split;

    int bit;
    if (this._low < this._range) {
      states[index] = this._zeroState[state];
      bit = 0;
    } else {
      this._low -= this._range;
      this._range = split;
      states[index] = this._oneState[state];
      bit = 1;
    }

    if (this._range >= 0x100)
      return bit;

    this._range <<= 8;
    this._low <<= 8;
    if (this._end)
      return bit;

    this._low += this._Next();
    if (this._position >= this._data.Length)
      this._end = true;

    return bit;
  }

  /// <summary>
  /// Reads a whole number, signed or not, out of one context's thirty-two states.
  /// </summary>
  /// <remarks>
  /// State 0 says whether the number is zero at all. States 1 to 10 carry the exponent as a unary
  /// run, states 22 to 31 the mantissa bits below it, and states 11 to 21 the sign. Each of the
  /// three runs stops sharing a state once it has gone far enough — <c>min(e, 9)</c> and its like —
  /// so a long value's tail all learns together instead of splitting the statistics thinner and
  /// thinner.
  /// </remarks>
  internal int Symbol(byte[] states, bool signed) {
    if (this.Get(states, 0) != 0)
      return 0;

    var exponent = 0;
    while (this.Get(states, 1 + Math.Min(exponent, 9)) != 0) {
      ++exponent;
      if (exponent > 31)
        throw new InvalidDataException("A coded number states an exponent larger than any value it could be.");
    }

    var magnitude = 1;
    for (var i = exponent - 1; i >= 0; --i)
      magnitude = magnitude * 2 + this.Get(states, 22 + Math.Min(i, 9));

    if (!signed)
      return magnitude;

    return this.Get(states, 11 + Math.Min(exponent, 10)) != 0 ? -magnitude : magnitude;
  }

  /// <summary>
  /// Reads the symbol that marks the end of a range-coded run, whose value means nothing.
  /// </summary>
  /// <remarks>
  /// The specification's sentinel termination: a bit coded against state 129 and thrown away, whose
  /// only purpose is that reading it leaves the coder exactly one byte past the end of what it
  /// coded. That is how a slice whose samples are Golomb coded knows which byte its bits start at,
  /// since the header before them is range coded and a range coder reads ahead.
  /// </remarks>
  internal void ReadTerminator() {
    var sentinel = new byte[] { 129 };
    this.Get(sentinel, 0);
  }
}
