using System;
using System.IO;

namespace FileFormat.Codecs.H265;

/// <summary>
/// The context-adaptive binary arithmetic decoder — ITU-T H.265, clause 9.3.4.3.
/// </summary>
/// <remarks>
/// Three ways of decoding one bin, and every syntax element is a string of them. A
/// <see cref="DecodeBin">context-coded</see> bin consults a probability estimate that this bin's own
/// history has tuned; a <see cref="DecodeBypass">bypassed</see> one assumes even odds and costs
/// exactly one bit, which is right for a sign or for the tail of a large value; a
/// <see cref="DecodeTerminate">terminating</see> one is a context-coded bin with a fixed, tiny
/// probability, spent where the answer is almost always no — is this the last block of the slice.
/// <para/>
/// The state transition is table-driven and the tables are normative. Sixty-four probability states,
/// each with the sub-range it gives up to the less probable symbol at four quantisations of the
/// current interval, and each with where it moves next depending on which symbol arrived. The tables
/// are not an approximation of anything a decoder is free to compute differently: two decoders that
/// disagree on one entry diverge on the next bin and never resynchronise.
/// <para/>
/// The state is a struct rather than a class because a slice decodes hundreds of thousands of bins
/// through it and every one of them touches three fields.
/// <para/>
/// The probability tables are in <see cref="H265CabacTables"/>, shared with
/// <see cref="H265CabacEncoder"/> so the two directions cannot hold different numbers.
/// </remarks>
internal struct H265CabacEngine {

  private readonly byte[] _data;
  private int _bitPosition;
  private int _range;
  private int _offset;

  /// <summary>
  /// The context states: the probability state in the upper seven bits, the more probable symbol in
  /// the lowest.
  /// </summary>
  /// <remarks>
  /// Packed into one byte per context rather than kept as two arrays because the two are read and
  /// written together every time, and the array is copied whole whenever a row of coding tree blocks
  /// hands its state to the row below.
  /// </remarks>
  private readonly byte[] _states;

  internal H265CabacEngine(byte[] data, byte[] states) {
    this._data = data;
    this._states = states;
    this._bitPosition = 0;
    this._range = 510;
    this._offset = 0;
  }

  /// <summary>The context states this engine is using, for the copy a synchronised row makes of them.</summary>
  internal readonly byte[] States => this._states;

  /// <summary>How many bits of the substream the arithmetic decoder has drawn.</summary>
  internal readonly int BitPosition => this._bitPosition;

  /// <summary>
  /// Starts the arithmetic decoder at a byte boundary — clause 9.3.2.5.
  /// </summary>
  /// <remarks>
  /// Nine bits, because the interval starts at 510 and the offset has to be able to name any point
  /// inside it. The two values it may not take are the ones at or past the interval's top: an
  /// encoder cannot produce them, so finding one means the substream did not start here.
  /// </remarks>
  internal void Start(int byteOffset) {
    if (byteOffset > this._data.Length)
      throw new InvalidDataException(
        $"An H.265 entropy-coded substream is said to start at byte {byteOffset} of a slice segment that is only "
        + $"{this._data.Length} bytes long. The entry point offsets in the slice header do not describe this NAL "
        + "unit.");

    this._bitPosition = byteOffset << 3;
    this._range = 510;
    this._offset = this._ReadBits(9);

    if (this._offset >= 510)
      throw new InvalidDataException(
        $"An H.265 entropy-coded substream opens with the nine-bit value {this._offset}, which clause 9.3.2.5 says "
        + "shall not occur — no encoder can produce it. The substream does not begin at this byte.");
  }

  /// <summary>One bin decoded against a context that its own history has tuned — clause 9.3.4.3.2.</summary>
  internal int DecodeBin(int contextIndex) {
    var state = this._states[contextIndex];
    var stateIdx = state >> 1;
    var mps = state & 1;

    // The interval is quantised to four buckets by its top two significant bits, so that one table
    // of sixty-four states by four widths covers every interval the decoder can be in.
    var lpsRange = H265CabacTables.RangeLps[(stateIdx << 2) | ((this._range >> 6) & 3)];
    this._range -= lpsRange;

    int bin;
    if (this._offset >= this._range) {
      bin = 1 - mps;
      this._offset -= this._range;
      this._range = lpsRange;

      // At state zero the two symbols are equally probable, so the less probable one arriving is
      // what flips which of them is called more probable.
      if (stateIdx == 0)
        mps = 1 - mps;

      this._states[contextIndex] = (byte)((H265CabacTables.TransitionLps[stateIdx] << 1) | mps);
    } else {
      bin = mps;
      this._states[contextIndex] = (byte)((H265CabacTables.TransitionMps[stateIdx] << 1) | mps);
    }

    this._Renormalize();
    return bin;
  }

  /// <summary>One bin at even odds, costing exactly one bit — clause 9.3.4.3.4.</summary>
  internal int DecodeBypass() {
    this._offset = (this._offset << 1) | this._ReadBit();

    if (this._offset < this._range)
      return 0;

    this._offset -= this._range;
    return 1;
  }

  /// <summary>Several bypassed bins as one unsigned number, most significant first.</summary>
  internal int DecodeBypassBits(int count) {
    var value = 0;
    for (var i = 0; i < count; ++i)
      value = (value << 1) | this.DecodeBypass();

    return value;
  }

  /// <summary>
  /// The bin that says whether this is the end — clause 9.3.4.3.5.
  /// </summary>
  /// <remarks>
  /// Not a context at all but a fixed interval of two out of the current range, which is as close to
  /// free as the coder gets. The renormalisation is skipped when it says yes, because there is
  /// nothing left to decode and the bits it would have consumed are the ones that terminate the
  /// payload.
  /// </remarks>
  internal int DecodeTerminate() {
    this._range -= 2;

    if (this._offset >= this._range)
      return 1;

    this._Renormalize();
    return 0;
  }

  private void _Renormalize() {
    while (this._range < 256) {
      this._range <<= 1;
      this._offset = (this._offset << 1) | this._ReadBit();
    }
  }

  /// <summary>
  /// One bit of the substream, or a zero past its end.
  /// </summary>
  /// <remarks>
  /// Reading past the end is not an error here and must not be. The arithmetic decoder holds nine
  /// bits of lookahead, so decoding the last bin of a slice legitimately asks for bits the encoder
  /// never wrote — they are past the payload's stop bit and cannot change any bin's value. Throwing
  /// there would refuse every stream whose final coding tree block ends near the byte boundary.
  /// </remarks>
  private int _ReadBit() {
    var position = this._bitPosition;
    if (position >= this._data.Length << 3) {
      this._bitPosition = position + 1;
      return 0;
    }

    this._bitPosition = position + 1;
    return (this._data[position >> 3] >> (7 - (position & 7))) & 1;
  }

  private int _ReadBits(int count) {
    var value = 0;
    for (var i = 0; i < count; ++i)
      value = (value << 1) | this._ReadBit();

    return value;
  }
}
