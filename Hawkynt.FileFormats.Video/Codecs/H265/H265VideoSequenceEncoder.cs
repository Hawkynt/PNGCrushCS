using System;
using System.Collections.Generic;
using FileFormat.Core;

namespace FileFormat.Codecs.H265;

/// <summary>
/// Codes a sequence of pictures: an IDR at the head of each group, predicted pictures after it.
/// </summary>
/// <remarks>
/// The head of a group is coded without reference to anything, so a decoder can start there; the
/// pictures after it are coded against the one before, which is where the saving is. That is the
/// whole point of the arrangement — an IDR every picture is a sequence of stills, and a sequence of
/// stills is not video compression however good each still is.
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

  /// <summary>How many predicted pictures follow an IDR before the next one.</summary>
  private const int _GROUP_LENGTH = 12;

  /// <summary>The quantiser predicted pictures code their residual at.</summary>
  private const int _QUANTISER = 26;

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
  private int _pictureCount;

  internal H265VideoSequenceEncoder(int width, int height) {
    this._displayWidth = (width + 1) & ~1;
    this._displayHeight = (height + 1) & ~1;
    this._codedWidth = H265PcmStillCodec._RoundUp(this._displayWidth, H265PcmStillCodec._CTB_SIZE);
    this._codedHeight = H265PcmStillCodec._RoundUp(this._displayHeight, H265PcmStillCodec._CTB_SIZE);

    var level = H265PcmStillCodec._SmallestLevelFor(this._codedWidth, this._codedHeight);
    this._vps = H265PcmStillCodec._MakeNal(H265NalUnitType.VideoParameterSet, H265PcmStillCodec._BuildVps(level));

    // Two pictures in the buffer rather than one, because a predicted picture is decoded while the
    // one it predicts from is still held.
    var sps = H265PcmStillCodec._BuildSps(
      this._codedWidth, this._codedHeight, this._displayWidth, this._displayHeight, level,
      maxDecPicBufferingMinus1: 1, log2MaxPocLsbMinus4: _LOG2_MAX_POC_LSB_MINUS4);
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

  /// <summary>Whether the next picture will be coded as an IDR rather than predicted.</summary>
  internal bool NextIsKeyFrame => this._reference == null || this._pictureCount % _GROUP_LENGTH == 0;

  /// <summary>Codes one picture and returns the access unit as a length-prefixed sample.</summary>
  internal (byte[] Sample, bool IsKeyFrame) Encode(RawImage source) {
    var planes = this._ToCodedPlanes(source);
    var keyFrame = this.NextIsKeyFrame;

    var slice = keyFrame ? this._EncodeKeyPicture(planes) : this._EncodePredictedPicture(planes);
    ++this._pictureCount;

    var sample = new byte[4 + slice.Length];
    sample[0] = (byte)(slice.Length >> 24);
    sample[1] = (byte)(slice.Length >> 16);
    sample[2] = (byte)(slice.Length >> 8);
    sample[3] = (byte)slice.Length;
    slice.CopyTo(sample, 4);
    return (sample, keyFrame);
  }

  private byte[] _EncodeKeyPicture((byte[] Y, byte[] Cb, byte[] Cr) planes) {
    var slice = H265PcmStillCodec._MakeNal(
      H265NalUnitType.IdrWithNoLeadingPictures,
      H265PcmStillCodec._BuildSlice(planes.Y, planes.Cb, planes.Cr, this._codedWidth, this._codedHeight));

    // Uncompressed coding units store their samples exactly and no filter runs over them, so the
    // reconstruction is the source. A predicted picture can be built on it without the encoder
    // having to decode what it just wrote.
    this._reference = this._ToPicture(planes, 0);
    this._pictureCount = 0;
    return slice;
  }

  private byte[] _EncodePredictedPicture((byte[] Y, byte[] Cb, byte[] Cr) planes) {
    var reference = this._reference
                    ?? throw new InvalidOperationException("A predicted picture needs a reference that was coded first.");

    var poc = this._pictureCount;
    var header = this._BuildPredictedSliceHeader(poc);
    var source = this._ToPicture(planes, poc);

    var parsedHeader = this._ParseSliceHeader(header);
    var encoder = new H265InterPictureEncoder(
      this._parsedSps, this._parsedPps, parsedHeader, source, reference, _QUANTISER);

    var data = encoder.Encode();
    var rbsp = new byte[header.Length + data.Length + 1];
    header.CopyTo(rbsp, 0);
    data.CopyTo(rbsp, header.Length);
    rbsp[^1] = 0x80; // rbsp_slice_segment_trailing_bits()

    this._reference = encoder.Reconstruction;
    return H265PcmStillCodec._MakeNal(H265NalUnitType.TrailingReference, rbsp);
  }

  /// <summary>
  /// The slice segment header of a predicted picture — clause 7.3.6.1.
  /// </summary>
  /// <remarks>
  /// The reference picture set is written into the slice rather than named from the sequence
  /// parameter set: one negative picture, the one immediately before this, used by this picture.
  /// That is the whole of the reference management a group of this shape needs.
  /// </remarks>
  private byte[] _BuildPredictedSliceHeader(int poc) {
    var w = new H265PcmStillCodec.Bits();
    w.WriteBit(1); // first_slice_segment_in_pic_flag
    w.WriteUe(0);  // slice_pic_parameter_set_id
    w.WriteUe(1);  // slice_type = P

    w.WriteBits((uint)(poc & ((1 << (_LOG2_MAX_POC_LSB_MINUS4 + 4)) - 1)), _LOG2_MAX_POC_LSB_MINUS4 + 4);
    w.WriteBit(0); // short_term_ref_pic_set_sps_flag

    // st_ref_pic_set(): one picture before this one, and this picture uses it.
    w.WriteUe(1);  // num_negative_pics
    w.WriteUe(0);  // num_positive_pics
    w.WriteUe(0);  // delta_poc_s0_minus1 => the picture at poc - 1
    w.WriteBit(1); // used_by_curr_pic_s0_flag

    w.WriteBit(0); // num_ref_idx_active_override_flag — the parameter set's one reference stands

    // five_minus_max_num_merge_cand: one candidate, so no merge index is coded at all.
    w.WriteUe(4);
    w.WriteSe(0);  // slice_qp_delta
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
