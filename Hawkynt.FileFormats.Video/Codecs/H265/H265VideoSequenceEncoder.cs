using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Codecs.H265;

/// <summary>
/// Codes a sequence of pictures: an IDR at the head of each group, then anchors with bidirectional
/// pictures between them.
/// </summary>
/// <remarks>
/// The head of a group is coded without reference to anything, so a decoder can start there. The
/// pictures after it are coded against their neighbours, which is where the saving is — an IDR every
/// picture is a sequence of stills, and a sequence of stills is not video compression however good
/// each still is.
/// <para/>
/// <b>Two of every three pictures are coded out of order.</b> A bidirectional picture may predict
/// from the picture after it, so that one has to be coded first: the anchors go out in display order
/// and the two pictures between a pair of them follow the later anchor. The decode order is
/// therefore I P B B P B B, and a container carries the difference as a decode timestamp beside the
/// presentation one. Nothing here reorders the pictures a caller hands in — they arrive in display
/// order and the encoder holds them back until the anchor they need exists.
/// <para/>
/// The IDR is coded as uncompressed coding units, which stores its samples exactly and makes the
/// reconstruction the predicted pictures build on identical to the source. The predicted pictures are
/// ordinary inter coding units with motion, a transform and quantised coefficients — see
/// <see cref="H265InterPictureEncoder"/>.
/// <para/>
/// Nothing filters the reconstruction: the picture parameter set disables deblocking and the sequence
/// parameter set disables sample-adaptive offset, so what this encoder holds as the reference is what
/// a decoder holds, without the encoder having to run the filters as well. That costs a little
/// quality at the block edges and removes a whole class of drift.
/// </remarks>
internal sealed class H265VideoSequenceEncoder {

  /// <summary>How many pictures a group holds, counting the IDR that opens it.</summary>
  private const int _GROUP_LENGTH = 12;

  /// <summary>How many bidirectional pictures sit between one anchor and the next.</summary>
  private const int _BIDIRECTIONAL_COUNT = 2;

  /// <summary>The quantiser inter pictures code their residual at.</summary>
  /// <remarks>
  /// Stated once and written into the slice header as a delta from the picture parameter set's 26.
  /// The encoder then quantises at the value it read back out of that header rather than at this
  /// constant: a writer that quantised at one number and told the decoder another would produce a
  /// picture that decodes to a scaled residual, which looks like a coding fault and is not one.
  /// </remarks>
  private const int _QUANTISER = 20;

  /// <summary>The quantiser the picture parameter set starts from — <c>init_qp_minus26</c> is zero.</summary>
  private const int _PARAMETER_SET_QUANTISER = 26;

  /// <summary>Enough count bits for a group many times longer than this encoder writes.</summary>
  private const int _LOG2_MAX_POC_LSB_MINUS4 = 4;

  private readonly int _displayWidth;
  private readonly int _displayHeight;
  private readonly int _codedWidth;
  private readonly int _codedHeight;
  private readonly byte[] _vps;
  private readonly byte[] _sps;
  private readonly byte[] _pps;
  private readonly H265SequenceParameterSet _parsedSps;
  private readonly H265PictureParameterSet _parsedPps;
  private readonly byte[] _configuration;

  private H265Picture? _reference;
  private int _referencePoc;
  private int _groupStart;
  private int _displayIndex;

  /// <summary>Pictures waiting for the anchor that follows them to be coded.</summary>
  private readonly List<Pending> _pending = [];

  /// <summary>Packets already coded, in the order a decoder reads them.</summary>
  private readonly Queue<Coded> _ready = new();

  /// <summary>The presentation times handed in, which become the decode times in coding order.</summary>
  private readonly Queue<long?> _decodeTimestamps = new();

  private readonly record struct Pending((byte[] Y, byte[] Cb, byte[] Cr) Planes, int DisplayIndex, long? Timestamp);

  /// <summary>One coded access unit and what a container needs to place it.</summary>
  internal readonly record struct Coded(byte[] Sample, bool IsKeyFrame, long? PresentationTimestamp, long? DecodeTimestamp);

  internal H265VideoSequenceEncoder(int width, int height) {
    this._displayWidth = (width + 1) & ~1;
    this._displayHeight = (height + 1) & ~1;
    this._codedWidth = H265PcmStillCodec._RoundUp(this._displayWidth, H265PcmStillCodec._CTB_SIZE);
    this._codedHeight = H265PcmStillCodec._RoundUp(this._displayHeight, H265PcmStillCodec._CTB_SIZE);

    var level = H265PcmStillCodec._SmallestLevelFor(this._codedWidth, this._codedHeight);
    this._vps = H265PcmStillCodec._MakeNal(H265NalUnitType.VideoParameterSet, H265PcmStillCodec._BuildVps(level));

    // The buffer holds the two anchors a bidirectional picture sits between plus the picture being
    // decoded, and the sequence reorders: the anchor is decoded before the pictures shown ahead of
    // it, so a decoder is told how many pictures it may have to hold back before it can output one.
    // Saying nothing there is not neutral -- a decoder told to reorder nothing outputs pictures in
    // the order it decoded them, which for this sequence is the wrong order.
    var sps = H265PcmStillCodec._BuildSps(
      this._codedWidth, this._codedHeight, this._displayWidth, this._displayHeight, level,
      maxDecPicBufferingMinus1: _BIDIRECTIONAL_COUNT + 1,
      log2MaxPocLsbMinus4: _LOG2_MAX_POC_LSB_MINUS4,
      maxNumReorderPics: _BIDIRECTIONAL_COUNT);
    this._sps = H265PcmStillCodec._MakeNal(H265NalUnitType.SequenceParameterSet, sps);
    this._pps = H265PcmStillCodec._MakeNal(H265NalUnitType.PictureParameterSet, H265PcmStillCodec._BuildPps());

    this._parsedSps = H265SequenceParameterSet.Parse(_Rbsp(this._sps));
    this._parsedPps = H265PictureParameterSet.Parse(_Rbsp(this._pps));
    this._configuration = H265PcmStillCodec._BuildDecoderConfiguration(this._vps, this._sps, this._pps, level);
  }

  /// <summary>The decoder configuration record a container carries beside the samples.</summary>
  internal byte[] Configuration => this._configuration;


  internal int DisplayWidth => this._displayWidth;

  internal int DisplayHeight => this._displayHeight;

  /// <summary>
  /// Takes one picture in display order and returns whatever is ready to go out.
  /// </summary>
  /// <remarks>
  /// A picture that a bidirectional one may predict from has to be coded before it, so most calls
  /// return nothing and one call in three returns the anchor and the two pictures behind it.
  /// </remarks>
  internal bool TryEncode(RawImage source, long? presentationTimestamp, out Coded coded) {
    var planes = this._ToCodedPlanes(source);
    var index = this._displayIndex++;
    this._decodeTimestamps.Enqueue(presentationTimestamp);

    if (this._reference == null || index % _GROUP_LENGTH == 0) {
      // A group boundary closes the one before it: anything still waiting has no anchor coming, so
      // it is coded against the one behind it rather than held past the picture it would predict from.
      this._DrainPendingAsPredicted();
      this._ready.Enqueue(this._Wrap(this._EncodeKeyPicture(planes, index), keyFrame: true, presentationTimestamp));
      return this._ready.TryDequeue(out coded);
    }

    this._pending.Add(new(planes, index, presentationTimestamp));
    if (this._pending.Count > _BIDIRECTIONAL_COUNT)
      this._EncodeGroup();

    return this._ready.TryDequeue(out coded);
  }

  /// <summary>Takes the pictures the encoder is still holding once the input has run out.</summary>
  internal IEnumerable<Coded> Flush() {
    this._DrainPendingAsPredicted();

    while (this._ready.TryDequeue(out var ready))
      yield return ready;
  }

  /// <summary>Codes the anchor that the waiting pictures predict forward from, then those pictures.</summary>
  private void _EncodeGroup() {
    var anchor = this._pending[^1];
    var past = this._reference!;
    var pastPoc = this._referencePoc;

    var future = this._EncodeAnchor(anchor);

    foreach (var pending in this._pending)
      if (pending.DisplayIndex != anchor.DisplayIndex)
        this._ready.Enqueue(this._Wrap(
          this._EncodeBidirectionalPicture(pending, past, pastPoc, future, anchor.DisplayIndex),
          keyFrame: false, pending.Timestamp));

    this._pending.Clear();
  }

  /// <summary>Codes what is left as predicted pictures, for a tail with no anchor after it.</summary>
  private void _DrainPendingAsPredicted() {
    foreach (var pending in this._pending)
      this._EncodeAnchor(pending);

    this._pending.Clear();
  }

  private H265Picture _EncodeAnchor(Pending anchor) {
    var slice = this._EncodePredictedPicture(anchor.Planes, anchor.DisplayIndex);
    this._ready.Enqueue(this._Wrap(slice, keyFrame: false, anchor.Timestamp));
    return this._reference!;
  }

  private Coded _Wrap(byte[] slice, bool keyFrame, long? presentationTimestamp) {
    var sample = new byte[4 + slice.Length];
    sample[0] = (byte)(slice.Length >> 24);
    sample[1] = (byte)(slice.Length >> 16);
    sample[2] = (byte)(slice.Length >> 8);
    sample[3] = (byte)slice.Length;
    slice.CopyTo(sample, 4);

    // The decode times are the presentation times handed in, taken in coding order: a picture coded
    // third is decoded at the third time, whatever it is shown at.
    this._decodeTimestamps.TryDequeue(out var decodeTimestamp);
    return new(sample, keyFrame, presentationTimestamp, decodeTimestamp);
  }

  private byte[] _EncodeKeyPicture((byte[] Y, byte[] Cb, byte[] Cr) planes, int displayIndex) {
    var slice = H265PcmStillCodec._MakeNal(
      H265NalUnitType.IdrWithNoLeadingPictures,
      H265PcmStillCodec._BuildSlice(planes.Y, planes.Cb, planes.Cr, this._codedWidth, this._codedHeight));

    // Uncompressed coding units store their samples exactly and no filter runs over them, so the
    // reconstruction is the source. A predicted picture can be built on it without the encoder
    // having to decode what it just wrote.
    this._reference = this._ToPicture(planes, 0);
    this._referencePoc = 0;
    this._groupStart = displayIndex;
    return slice;
  }

  private byte[] _EncodePredictedPicture((byte[] Y, byte[] Cb, byte[] Cr) planes, int displayIndex) {
    var reference = this._reference
                    ?? throw new InvalidOperationException("A predicted picture needs a reference that was coded first.");

    var poc = displayIndex - this._groupStart;
    var header = this._BuildSliceHeader(H265SliceType.P, poc, this._referencePoc, futurePoc: null);
    var source = this._ToPicture(planes, poc);

    var parsed = this._ParseSliceHeader(header);
    var encoder = new H265InterPictureEncoder(
      this._parsedSps, this._parsedPps, parsed, source, reference, null, parsed.SliceQpY);

    var slice = _Assemble(header, encoder.Encode(), H265NalUnitType.TrailingReference);
    this._reference = encoder.Reconstruction;
    this._referencePoc = poc;
    return slice;
  }

  /// <summary>
  /// Codes a picture between two anchors, which may take each block from either or from both.
  /// </summary>
  /// <remarks>
  /// It is coded as a non-reference picture — nothing predicts from it, so it is not kept and never
  /// appears in another picture's reference set. That is what makes the quality of a bidirectional
  /// picture a matter of that picture alone: an error in one cannot reach the next.
  /// </remarks>
  private byte[] _EncodeBidirectionalPicture(
    Pending pending, H265Picture past, int pastPoc, H265Picture future, int futureDisplayIndex) {
    var poc = pending.DisplayIndex - this._groupStart;
    var futurePoc = futureDisplayIndex - this._groupStart;
    var header = this._BuildSliceHeader(H265SliceType.B, poc, pastPoc, futurePoc);
    var source = this._ToPicture(pending.Planes, poc);

    var parsed = this._ParseSliceHeader(header);
    var encoder = new H265InterPictureEncoder(
      this._parsedSps, this._parsedPps, parsed, source, past, future, parsed.SliceQpY);

    return _Assemble(header, encoder.Encode(), H265NalUnitType.TrailingNonReference);
  }

  private static byte[] _Assemble(byte[] header, byte[] data, H265NalUnitType type) {
    // The arithmetic coder already wrote the trailing bits: the stop bit has to sit immediately
    // after the bits it committed rather than in a byte appended behind them.
    var rbsp = new byte[header.Length + data.Length];
    header.CopyTo(rbsp, 0);
    data.CopyTo(rbsp, header.Length);

    return H265PcmStillCodec._MakeNal(type, rbsp);
  }

  /// <summary>
  /// The slice segment header of an inter picture — clause 7.3.6.1.
  /// </summary>
  /// <remarks>
  /// The reference picture set is written into the slice rather than named from the sequence
  /// parameter set, and it is what keeps the anchors alive: a predicted picture names the anchor
  /// behind it, a bidirectional one names the anchors on both sides. Anything a picture does not
  /// name is dropped from the buffer, so the set is the reference management rather than a
  /// description of it.
  /// </remarks>
  private byte[] _BuildSliceHeader(H265SliceType type, int poc, int pastPoc, int? futurePoc) {
    var w = new H265PcmStillCodec.Bits();
    w.WriteBit(1); // first_slice_segment_in_pic_flag
    w.WriteUe(0);  // slice_pic_parameter_set_id
    w.WriteUe(type == H265SliceType.B ? 0u : 1u); // slice_type

    w.WriteBits((uint)(poc & ((1 << (_LOG2_MAX_POC_LSB_MINUS4 + 4)) - 1)), _LOG2_MAX_POC_LSB_MINUS4 + 4);
    w.WriteBit(0); // short_term_ref_pic_set_sps_flag

    // st_ref_pic_set()
    w.WriteUe(1);  // num_negative_pics
    w.WriteUe(futurePoc.HasValue ? 1u : 0u); // num_positive_pics
    w.WriteUe((uint)(poc - pastPoc - 1)); // delta_poc_s0_minus1
    w.WriteBit(1); // used_by_curr_pic_s0_flag
    if (futurePoc.HasValue) {
      w.WriteUe((uint)(futurePoc.Value - poc - 1)); // delta_poc_s1_minus1
      w.WriteBit(type == H265SliceType.B ? 1 : 0); // used_by_curr_pic_s1_flag
    }

    w.WriteBit(0); // num_ref_idx_active_override_flag — the parameter set's one per list stands
    if (type == H265SliceType.B)
      w.WriteBit(0); // mvd_l1_zero_flag


    // five_minus_max_num_merge_cand: one candidate, so no merge index is coded at all.
    w.WriteUe(4);
    w.WriteSe(_QUANTISER - _PARAMETER_SET_QUANTISER); // slice_qp_delta
    w.WriteByteAlignment();
    return w.ToArray();
  }

  /// <summary>
  /// Parses the header this encoder just wrote, which is what the rest of the encoder runs on.
  /// </summary>
  /// <remarks>
  /// Deliberately not built as an object and written from it. The motion derivation reads the header
  /// to decide what a merge candidate is and which reference a vector points into, and a decoder
  /// reads those from the bits. Reading them back the same way is what makes the encoder's model of
  /// the slice the decoder's model of it rather than a second statement of the same intent.
  /// </remarks>
  private H265SliceHeader _ParseSliceHeader(byte[] header) {
    var rbsp = new byte[header.Length + 1];
    header.CopyTo(rbsp, 0);
    rbsp[^1] = 0x80;

    var unit = new H265NalUnit(H265NalUnitType.TrailingReference, 0, 0, rbsp, []);

    return H265SliceHeader.Parse(
      unit,
      new Dictionary<int, H265SequenceParameterSet> { [0] = this._parsedSps },
      new Dictionary<int, H265PictureParameterSet> { [0] = this._parsedPps });
  }

  private (byte[] Y, byte[] Cb, byte[] Cr) _ToCodedPlanes(RawImage source) {
    var even = source.Width == this._displayWidth && source.Height == this._displayHeight
      ? source
      : H265PcmStillCodec._PadRgbToEven(source, this._displayWidth, this._displayHeight);

    var yuv = FastRawImageConverter.Convert(even, PixelFormat.Yuv420P8);
    return H265PcmStillCodec._PadYuvToCodedSize(yuv, this._codedWidth, this._codedHeight);
  }

  private H265Picture _ToPicture((byte[] Y, byte[] Cb, byte[] Cr) planes, int poc) {
    var picture = new H265Picture(this._codedWidth, this._codedHeight, 2) {
      PictureOrderCount = poc,
      Marking = H265ReferenceMarking.ShortTerm,
    };

    for (var i = 0; i < picture.Luma.Length; ++i)
      picture.Luma[i] = planes.Y[i];

    for (var i = 0; i < picture.Cb.Length; ++i) {
      picture.Cb[i] = planes.Cb[i];
      picture.Cr[i] = planes.Cr[i];
    }

    return picture;
  }

  /// <summary>The payload of a NAL unit, without its two-byte header or emulation prevention.</summary>
  private static byte[] _Rbsp(byte[] nal) {
    var result = new List<byte>(nal.Length);
    var zeroes = 0;

    for (var i = 2; i < nal.Length; ++i) {
      var octet = nal[i];
      if (zeroes >= 2 && octet == 3) {
        zeroes = 0;
        continue;
      }

      result.Add(octet);
      zeroes = octet == 0 ? zeroes + 1 : 0;
    }

    return [.. result];
  }
}
