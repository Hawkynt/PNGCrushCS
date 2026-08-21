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
/// </remarks>
internal struct H265CabacEngine {

  /// <summary>
  /// Table 9-46: what the interval gives up to the less probable symbol, by state and by how wide
  /// the interval currently is.
  /// </summary>
  private static readonly byte[] _RangeTableLps = [
    128, 176, 208, 240, 128, 167, 197, 227, 128, 158, 187, 216, 123, 150, 178, 205,
    116, 142, 169, 195, 111, 135, 160, 185, 105, 128, 152, 175, 100, 122, 144, 166,
    95, 116, 137, 158, 90, 110, 130, 150, 85, 104, 123, 142, 81, 99, 117, 135,
    77, 94, 111, 128, 73, 89, 105, 122, 69, 85, 100, 116, 66, 80, 95, 110,
    62, 76, 90, 104, 59, 72, 86, 99, 56, 69, 81, 94, 53, 65, 77, 89,
    51, 62, 73, 85, 48, 59, 69, 80, 46, 56, 66, 76, 43, 53, 63, 72,
    41, 50, 59, 69, 39, 48, 56, 65, 37, 45, 54, 62, 35, 43, 51, 59,
    33, 41, 48, 56, 32, 39, 46, 53, 30, 37, 43, 50, 29, 35, 41, 48,
    27, 33, 39, 45, 26, 31, 37, 43, 24, 30, 35, 41, 23, 28, 33, 39,
    22, 27, 32, 37, 21, 26, 30, 35, 20, 24, 29, 33, 19, 23, 27, 31,
    18, 22, 26, 30, 17, 21, 25, 28, 16, 20, 23, 27, 15, 19, 22, 25,
    14, 18, 21, 24, 14, 17, 20, 23, 13, 16, 19, 22, 12, 15, 18, 21,
    12, 14, 17, 20, 11, 14, 16, 19, 11, 13, 15, 18, 10, 12, 15, 17,
    10, 12, 14, 16, 9, 11, 13, 15, 9, 11, 12, 14, 8, 10, 12, 14,
    8, 9, 11, 13, 7, 9, 11, 12, 7, 9, 10, 12, 7, 8, 10, 11,
    6, 8, 9, 11, 6, 7, 9, 10, 6, 7, 8, 9, 2, 2, 2, 2,
  ];

  /// <summary>
  /// Table 9-47: where a state moves when the less probable symbol arrives.
  /// </summary>
  /// <remarks>
  /// It moves several steps at once, and further the more confident it was, because being wrong when
  /// confident is stronger evidence than being right when unsure. State 63 stays put: it is the
  /// terminating state, whose interval is fixed at two.
  /// </remarks>
  private static readonly byte[] _TransitionLps = [
    0, 0, 1, 2, 2, 4, 4, 5, 6, 7, 8, 9, 9, 11, 11, 12,
    13, 13, 15, 15, 16, 16, 18, 18, 19, 19, 21, 21, 22, 22, 23, 24,
    24, 25, 26, 26, 27, 27, 28, 29, 29, 30, 30, 30, 31, 32, 32, 33,
    33, 33, 34, 34, 35, 35, 35, 36, 36, 36, 37, 37, 37, 38, 38, 63,
  ];

  /// <summary>Table 9-47: where a state moves when the more probable symbol arrives — one step up.</summary>
  private static readonly byte[] _TransitionMps = [
    1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
    17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32,
    33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48,
    49, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 62, 62, 63,
  ];

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
    var lpsRange = _RangeTableLps[(stateIdx << 2) | ((this._range >> 6) & 3)];
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

      this._states[contextIndex] = (byte)((_TransitionLps[stateIdx] << 1) | mps);
    } else {
      bin = mps;
      this._states[contextIndex] = (byte)((_TransitionMps[stateIdx] << 1) | mps);
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
