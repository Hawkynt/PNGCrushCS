using System.Collections.Generic;

namespace FileFormat.Codecs.H265;

/// <summary>
/// The context-adaptive binary arithmetic encoder — the write direction of ITU-T H.265, clause 9.3.4.
/// </summary>
/// <remarks>
/// The decoder in <see cref="H265CabacEngine"/> narrows an interval it is given and reads the bits
/// that tell it which way; this narrows the same interval the same way and writes them. Both run the
/// same normative state machine out of <see cref="H265CabacTables"/> and both update the same context
/// array, so a bin written here decodes there to the value it was given — which is the only property
/// worth having and the one the round-trip test asserts.
/// <para/>
/// Two things make the write direction harder than the read direction, and both are about the low end
/// of the interval rather than its width.
/// <para/>
/// <b>Carry.</b> Adding to the low end can carry into bits that were decided long ago. A byte of all
/// ones cannot absorb a carry, so it cannot be emitted while any carry into it is still possible.
/// Runs of them are counted instead and resolved when the next byte that can absorb one appears: if a
/// carry arrived, the byte before the run goes out incremented and the run goes out as zeroes; if it
/// did not, the byte goes out unchanged and the run goes out as ones.
/// <para/>
/// <b>Bit budget.</b> The low end is held in a 32-bit accumulator with a running count of how much
/// room is left in it. Each renormalisation shift spends some, and when too little is left the top
/// byte is pushed towards the output and the room is reclaimed. The count starts at 23 rather than 32
/// because the accumulator has to hold nine bits of interval above the byte being assembled.
/// </remarks>
internal sealed class H265CabacEncoder {

  /// <summary>The bits of headroom the accumulator starts with, above the nine the interval occupies.</summary>
  private const int _INITIAL_BITS_LEFT = 23;

  /// <summary>Below this much headroom the top byte is pushed out to reclaim some.</summary>
  private const int _FLUSH_THRESHOLD = 12;

  private readonly List<byte> _bytes = [];
  private readonly byte[] _states;

  private uint _low;
  private int _range;
  private int _bitsLeft;

  /// <summary>The byte held back because a carry could still reach it, and the run of ones behind it.</summary>
  private int _bufferedByte;
  private int _bufferedCount;

  /// <summary>Starts an encoder over <paramref name="states"/>, which it shares and updates in place.</summary>
  internal H265CabacEncoder(byte[] states) {
    this._states = states;
    this._low = 0;
    this._range = 510;
    this._bitsLeft = _INITIAL_BITS_LEFT;
    this._bufferedByte = 0xFF;
    this._bufferedCount = 0;
  }

  /// <summary>The context states this encoder is using, for the copy a synchronised row makes of them.</summary>
  internal byte[] States => this._states;

  /// <summary>One bin coded against a context that its own history has tuned — clause 9.3.4.3.2.</summary>
  internal void EncodeBin(int contextIndex, int bin) {
    var state = this._states[contextIndex];
    var stateIndex = state >> 1;
    var mps = state & 1;

    var lpsRange = H265CabacTables.RangeLps[(stateIndex << 2) | ((this._range >> 6) & 3)];
    this._range -= lpsRange;

    if (bin != mps) {
      // The less probable symbol sits at the top of the interval, so the low end moves up by what is
      // left of the more probable one.
      var shift = H265CabacTables.RenormalizationShift(lpsRange);
      this._low = (this._low + (uint)this._range) << shift;
      this._range = lpsRange << shift;
      this._bitsLeft -= shift;

      // At state zero the two symbols are equally probable, so the less probable one arriving is what
      // flips which of them is called more probable.
      if (stateIndex == 0)
        mps = 1 - mps;

      this._states[contextIndex] = (byte)((H265CabacTables.TransitionLps[stateIndex] << 1) | mps);
      this._FlushIfTight();
      return;
    }

    this._states[contextIndex] = (byte)((H265CabacTables.TransitionMps[stateIndex] << 1) | mps);
    if (this._range >= 256)
      return;

    this._low <<= 1;
    this._range <<= 1;
    --this._bitsLeft;
    this._FlushIfTight();
  }

  /// <summary>One bin at even odds, costing exactly one bit — clause 9.3.4.3.4.</summary>
  internal void EncodeBypass(int bin) {
    this._low <<= 1;
    if (bin != 0)
      this._low += (uint)this._range;

    --this._bitsLeft;
    this._FlushIfTight();
  }

  /// <summary>Several bypassed bins as one unsigned number, most significant first.</summary>
  internal void EncodeBypassBits(int value, int count) {
    for (var i = count - 1; i >= 0; --i)
      this.EncodeBypass((value >> i) & 1);
  }

  /// <summary>
  /// The bin that says whether this is the end — clause 9.3.4.3.5.
  /// </summary>
  /// <remarks>
  /// A fixed interval of two out of the current range rather than a context, which is as close to
  /// free as the coder gets. Saying yes renormalises by seven in one step, which is what leaves the
  /// accumulator holding exactly the bits <see cref="Finish"/> has to write out.
  /// </remarks>
  internal void EncodeTerminate(int bin) {
    this._range -= 2;

    if (bin != 0) {
      this._low += (uint)this._range;
      this._low <<= 7;
      this._range = 2 << 7;
      this._bitsLeft -= 7;
    } else if (this._range >= 256) {
      return;
    } else {
      this._low <<= 1;
      this._range <<= 1;
      --this._bitsLeft;
    }

    this._FlushIfTight();
  }

  /// <summary>
  /// Closes the interval, writes the stop bit, and returns the bytes of the slice segment.
  /// </summary>
  /// <remarks>
  /// The accumulator's top bit is the last carry, and it has to be resolved before anything is
  /// written: if it is set, the byte held back goes out incremented and every held-back run of ones
  /// has wrapped to zero. What remains of the low end then names a point inside the final interval,
  /// and writing it is what commits every bin the interval was narrowed by.
  /// <para/>
  /// <b>The stop bit belongs here</b>, immediately after those bits and before the alignment zeroes,
  /// not in a byte the caller appends afterwards. The decoder holds nine bits of lookahead, so
  /// deciding the last terminating bin draws bits past the ones the encoder committed — and what it
  /// finds there decides the answer. A one there is what clause 9.3.4.3.5 leaves for it; zeroes let a
  /// short slice decode its own end-of-slice flag as nought, which reads as the entropy coder having
  /// gone out of step when nothing has.
  /// </remarks>
  internal byte[] Finish() {
    if ((this._low >> (32 - this._bitsLeft)) != 0) {
      this._EmitBuffered(carry: 1);
      this._low -= 1u << (32 - this._bitsLeft);
    } else {
      this._EmitBuffered(carry: 0);
    }

    var bits = 24 - this._bitsLeft;
    var value = this._low >> 8;
    for (var i = bits - 1; i >= 0; --i)
      this._WriteBit((int)((value >> i) & 1));

    // rbsp_slice_segment_trailing_bits(): the stop bit, then zeroes to the byte boundary.
    this._WriteBit(1);
    this._AlignToByte();
    return [.. this._bytes];
  }

  private int _partialByte;
  private int _partialBits;

  private void _FlushIfTight() {
    if (this._bitsLeft >= _FLUSH_THRESHOLD)
      return;

    // The byte above the accumulator's working window is decided except for a carry that a later
    // addition might still send into it.
    var leadByte = (int)(this._low >> (24 - this._bitsLeft));
    this._bitsLeft += 8;
    this._low &= 0xFFFFFFFFu >> this._bitsLeft;

    if (leadByte == 0xFF) {
      // All ones cannot absorb a carry: hold it and let the next byte that can decide which way it
      // and everything held behind it go out.
      ++this._bufferedCount;
      return;
    }

    // This byte can absorb a carry, so it settles every byte held before it. A carry out of it turns
    // the run of ones behind into zeroes and increments the byte in front.
    if (this._bufferedCount > 0)
      this._EmitBuffered(leadByte >> 8);

    this._bufferedByte = leadByte & 0xFF;
    this._bufferedCount = 1;
  }

  /// <summary>Writes out the byte held back and the run of ones behind it, now that the carry is known.</summary>
  private void _EmitBuffered(int carry) {
    if (this._bufferedCount <= 0)
      return;

    this._EmitByte((this._bufferedByte + carry) & 0xFF);

    var run = (0xFF + carry) & 0xFF;
    while (--this._bufferedCount > 0)
      this._EmitByte(run);

    this._bufferedByte = 0xFF;
    this._bufferedCount = 0;
  }

  private void _EmitByte(int value) {
    for (var i = 7; i >= 0; --i)
      this._WriteBit((value >> i) & 1);
  }

  private void _WriteBit(int bit) {
    this._partialByte = (this._partialByte << 1) | bit;
    if (++this._partialBits != 8)
      return;

    this._bytes.Add((byte)this._partialByte);
    this._partialByte = 0;
    this._partialBits = 0;
  }

  private void _AlignToByte() {
    while (this._partialBits != 0)
      this._WriteBit(0);
  }
}
