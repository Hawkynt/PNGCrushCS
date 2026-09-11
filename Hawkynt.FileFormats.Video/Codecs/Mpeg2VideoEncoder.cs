using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Codecs.Mpeg;
using FileFormat.Core;

namespace FileFormat.Codecs;

/// <summary>
/// Encodes progressive MPEG-2 video (ITU-T H.262 / ISO/IEC 13818-2) as Main Profile at Main Level,
/// 8-bit 4:2:0 I and P pictures.
/// </summary>
/// <remarks>
/// A group of pictures opens with an I picture and continues with P pictures predicted forwards from
/// the anchor before them, one slice per macroblock row, using the default quantisation matrices,
/// linear quantiser scale, zig-zag scan, Table B.14 and eight-bit intra DC precision. Those are all
/// standard choices, so a decoder sees ordinary MPEG-2 rather than a private subset on the wire.
/// Coding order equals display order, so no packet is reordered.
/// <para/>
/// The reference a P picture predicts from is read back out of a decoder this encoder drives with its
/// own output, so prediction starts from the samples the receiving decoder will hold rather than from
/// the source frame — the two differ by the quantiser's loss, and an encoder that ignores the
/// difference is correct on its first predicted picture and a little further out on every one after.
/// <para/>
/// Interlacing, field pictures, dual-prime prediction and B pictures are not written. B pictures in
/// particular would reorder coding against display, which changes what <see cref="TryEncode"/> hands
/// its caller rather than only what is written. Reading all of them is supported in full.
/// <para/>
/// <b>Lossy.</b> H.262 quantises DCT coefficients and has no lossless mode. The encoder begins at
/// quantiser_scale_code 4 and raises it only when necessary to remain inside Main Level's 15 Mbit/s
/// rate bound. A picture that still cannot fit at code 31 is refused rather than labelled Main Level
/// while violating the level it declares.
/// <para/>
/// Every packet carries a repeated sequence header and sequence extension before its picture. H.262
/// explicitly permits sequence headers to repeat, and doing so makes every packet this encoder marks
/// as a key frame independently decodable by the elementary-stream reader as well as by containers.
/// A P picture repeats them too: it is not independently decodable and does not claim to be, but a
/// decoder that joins the stream at one still learns the geometry it needs to be ready for the next
/// intra picture.
/// </remarks>
[VerifiedBy(ConformanceOracle.FFmpeg)]
public sealed class Mpeg2VideoEncoder : IVideoCodecEncoder<Mpeg2VideoEncoder> {

  private static readonly CodecTag _MPG2 = CodecTag.FromCharacters("MPG2");

  private const int _MAX_WIDTH = 720;
  private const int _MAX_HEIGHT = 576;
  private const long _MAX_LUMA_SAMPLE_RATE = 10_368_000;
  private const long _MAX_BIT_RATE = 15_000_000;
  private const int _VBV_BUFFER_SIZE_VALUE = 112; // 112 * 16,384 = 1,835,008 bits, MP@ML's bound.
  private const int _BIT_RATE_VALUE = (int)(_MAX_BIT_RATE / 400);

  private static readonly int[] _QuantiserScaleCandidates = [4, 6, 8, 12, 16, 20, 24, 28, 31];

  /// <summary>Pictures per group: one I picture and eleven P pictures.</summary>
  /// <remarks>
  /// Twelve bounds error propagation — nothing predicted is more than eleven pictures from an
  /// independently decodable one — and is the length the constrained-parameters material uses.
  /// </remarks>
  private const int _GROUP_SIZE = 12;

  /// <summary>f_code for both forward components.</summary>
  /// <remarks>
  /// MPEG-2 has no <c>full_pel_forward_vector</c>: every vector counts half-samples, so f_code 3
  /// gives <c>f = 4</c> and a range of [-64, 63] half-samples, which is [-32, 31] whole pixels. The
  /// f_code is what decides how far anything may move, not merely what it costs to say so — 7.6.3.1
  /// folds the reconstructed vector into that range, so a vector beyond it comes back as a
  /// <em>different</em> vector rather than as an expensive one.
  /// </remarks>
  private const int _FORWARD_F_CODE = 3;

  /// <summary>f, the scale a motion difference is stated in.</summary>
  private const int _MOTION_SCALE = 1 << (_FORWARD_F_CODE - 1);

  /// <summary>The largest displacement the forward vectors can state, in half-samples.</summary>
  private const int _MOTION_LIMIT = 16 * _MOTION_SCALE;

  /// <summary>f_code 15 is 6.3.10's "this direction carries no vectors at all".</summary>
  private const int _F_CODE_UNUSED = 15;

  /// <summary>How far a motion search looks, in whole pixels, around the predicted vector.</summary>
  private const int _SEARCH_RANGE = 15;

  private readonly MediaStreamInfo _requested;
  private readonly int _width;
  private readonly int _height;
  private readonly int _codedWidth;
  private readonly int _codedHeight;
  private readonly int _macroblockWidth;
  private readonly int _macroblockHeight;
  private readonly int _frameRateCode;
  private readonly Rational _frameRate;

  private long _pictureNumber;
  private MediaStreamInfo? _stream;

  /// <summary>Driven on this encoder's own output, to hand back the reference a P picture predicts from.</summary>
  private readonly MpegVideoDecoder _reconstruction = new();

  /// <summary>How far into the current group the next picture is; zero codes an I picture.</summary>
  private int _groupPosition;

  private Mpeg2VideoEncoder(MediaStreamInfo stream, int frameRateCode, Rational frameRate) {
    this._requested = stream;
    this._width = stream.Width;
    this._height = stream.Height;
    this._codedWidth = (stream.Width + 15) & ~15;
    this._codedHeight = (stream.Height + 15) & ~15;
    this._macroblockWidth = this._codedWidth >> 4;
    this._macroblockHeight = this._codedHeight >> 4;
    this._frameRateCode = frameRateCode;
    this._frameRate = frameRate;
  }

  public static string CodecName => "MPEG-2 video (ISO/IEC 13818-2)";

  public static CodecTag Codec => _MPG2;

  /// <summary>Builds an intra-only Main-Profile/Main-Level MPEG-2 encoder.</summary>
  public static Mpeg2VideoEncoder Create(MediaStreamInfo stream) {
    ArgumentNullException.ThrowIfNull(stream);

    if (stream.Kind != MediaStreamKind.Video)
      throw new NotSupportedException("MPEG-2 video can only encode a video stream.");

    if (stream.Width is <= 0 or > _MAX_WIDTH || stream.Height is <= 0 or > _MAX_HEIGHT)
      throw new NotSupportedException(
        $"This MPEG-2 encoder writes Main Profile at Main Level, whose coded picture is at most {_MAX_WIDTH}x{_MAX_HEIGHT}; "
        + $"{stream.Width}x{stream.Height} was requested.");

    if ((stream.Width & 1) != 0 || (stream.Height & 1) != 0)
      throw new NotSupportedException(
        $"This MPEG-2 encoder writes 4:2:0 pictures, so both dimensions must be even; {stream.Width}x{stream.Height} was requested.");

    var (frameRateCode, frameRate) = _FrameRateOf(stream.FrameRate);
    var codedWidth = (stream.Width + 15) & ~15;
    var codedHeight = (stream.Height + 15) & ~15;
    var sampleRate = (Int128)codedWidth * codedHeight * frameRate.Numerator / frameRate.Denominator;
    if (sampleRate > _MAX_LUMA_SAMPLE_RATE)
      throw new NotSupportedException(
        $"A {stream.Width}x{stream.Height} picture at {frameRate} requires {sampleRate} luminance samples/s after "
        + $"macroblock padding; Main Level permits {_MAX_LUMA_SAMPLE_RATE}.");

    return new(stream, frameRateCode, frameRate);
  }

  /// <summary>Codes one picture: intra at the head of a group, forward-predicted otherwise.</summary>
  public bool TryEncode(RawImage frame, long? presentationTimestamp, out CodedPacket packet) {
    ArgumentNullException.ThrowIfNull(frame);

    if (frame.Width != this._width || frame.Height != this._height)
      throw new InvalidDataException(
        $"This MPEG-2 stream is {this._width}x{this._height}; a {frame.Width}x{frame.Height} picture arrived. "
        + "A sequence cannot change dimensions while the encoder is open.");

    var source = this._ToFrame(frame);

    // The first picture of every group is intra, and so is any picture with nothing to predict from.
    var reference = this._reconstruction.CurrentAnchor;
    var isIntra = this._groupPosition == 0 || reference == null;
    if (isIntra)
      reference = null;

    byte[]? bytes = null;
    foreach (var quantiserScaleCode in _QuantiserScaleCandidates) {
      bytes = this._EncodePicture(source, quantiserScaleCode, reference);
      if (_FitsMainLevel(bytes.Length, this._frameRate))
        break;

      bytes = null;
    }

    if (bytes == null)
      throw new InvalidDataException(
        $"The {this._width}x{this._height} MPEG-2 picture exceeds Main Level's {_MAX_BIT_RATE / 1_000_000} Mbit/s "
        + "rate bound even at quantiser_scale_code 31.");

    // Reconstruct by decoding what was just written, so the next P picture predicts from what its
    // decoder will hold. Only the bytes actually kept are fed back; the rate-control attempts above
    // are discarded.
    this._reconstruction.DecodePacket(bytes);

    packet = new(
      this._requested.Index,
      bytes,
      PresentationTimestamp: presentationTimestamp,
      DecodeTimestamp: presentationTimestamp,
      IsKeyFrame: isIntra);

    ++this._pictureNumber;
    this._groupPosition = (this._groupPosition + 1) % _GROUP_SIZE;
    return true;
  }

  /// <summary>Nothing is reordered or held back.</summary>
  public IEnumerable<CodedPacket> Flush() => [];

  /// <summary>The stream description required by MPEG elementary streams and ordinary containers.</summary>
  public MediaStreamInfo DescribeStream() => this._stream ??= new() {
    Index = this._requested.Index,
    Kind = MediaStreamKind.Video,
    Codec = _MPG2,
    Handler = _MPG2,
    CodecId = "V_MPEG2",
    TimeBase = this._requested.TimeBase,
    FrameRate = this._frameRate,
    Width = this._width,
    Height = this._height,
    BitsPerPixel = 12,
    CodecPrivateData = ReadOnlyMemory<byte>.Empty,
  };

  private byte[] _EncodePicture(MpegFrame source, int quantiserScaleCode, MpegFrame? reference) {
    var writer = new MpegBitWriter();
    this._WriteSequenceHeader(writer);
    _WriteSequenceExtension(writer);
    this._WritePictureHeader(writer, reference == null);
    _WritePictureCodingExtension(writer, reference == null);

    var dcPredictor = new int[3];
    var levels = new int[64 * 6];
    for (var row = 0; row < this._macroblockHeight; ++row) {
      writer.WriteStartCode((byte)(MpegStartCode.FirstSlice + row));
      writer.WriteBits(quantiserScaleCode, 5);
      writer.WriteBit(0); // extra_bit_slice

      // Both predictors reset at the head of every slice, which is what lets a decoder start at one.
      dcPredictor[0] = dcPredictor[1] = dcPredictor[2] = 128;
      var predictedX = 0;
      var predictedY = 0;
      var pendingSkips = 0;

      for (var column = 0; column < this._macroblockWidth; ++column) {
        if (reference == null) {
          MpegVlcTables.MacroblockAddressIncrement.Write(writer, 1);
          MpegVlcTables.IntraMacroblockType.Write(writer, MpegVlcTables.TypeIntra);

          for (var block = 0; block < 6; ++block)
            this._WriteIntraBlock(writer, source, column, row, block, quantiserScaleCode, dcPredictor);

          continue;
        }

        var (vectorX, vectorY) = this._SearchMotion(source, reference, column, row, predictedX, predictedY);
        var pattern = this._QuantiseResidual(
          source, reference, column, row, vectorX, vectorY, quantiserScaleCode, levels);

        // A skipped macroblock is exactly a zero vector and no residual, and is not written at all:
        // the next coded macroblock's address increment steps over it. The first and last macroblock
        // of a slice are always coded -- the first fixes where the slice starts, and a slice that
        // ended on a skip would not say where it ended.
        if (vectorX == 0 && vectorY == 0 && pattern == 0
            && column != 0 && column != this._macroblockWidth - 1) {
          ++pendingSkips;

          // Skipping resets the decoder's vector predictors, so the encoder's must follow.
          predictedX = 0;
          predictedY = 0;
          continue;
        }

        _WriteAddressIncrement(writer, pendingSkips + 1);
        pendingSkips = 0;

        MpegVlcTables.PredictedMacroblockType.Write(
          writer,
          pattern != 0
            ? MpegVlcTables.TypeMotionForward | MpegVlcTables.TypePattern
            : MpegVlcTables.TypeMotionForward);

        _WriteMotionCode(writer, vectorX - predictedX);
        _WriteMotionCode(writer, vectorY - predictedY);
        predictedX = vectorX;
        predictedY = vectorY;

        if (pattern == 0)
          continue;

        MpegVlcTables.CodedBlockPattern.Write(writer, pattern);
        for (var block = 0; block < 6; ++block)
          if ((pattern & (1 << (5 - block))) != 0)
            MpegInterBlockEncoder.Write(writer, levels.AsSpan(block * 64, 64), isMpeg2: true);
      }
    }

    return writer.ToArray();
  }

  /// <summary>Writes macroblock_address_increment, escaping the part above thirty-three.</summary>
  /// <remarks>
  /// Table B.1 states one to thirty-three directly. A longer step is written as macroblock_escape
  /// codes worth a further thirty-three each, then the remainder -- which is how a run of skipped
  /// macroblocks longer than the table is spelled.
  /// </remarks>
  private static void _WriteAddressIncrement(MpegBitWriter writer, int increment) {
    while (increment > 33) {
      MpegVlcTables.MacroblockAddressIncrement.Write(writer, MpegVlcTables.Escape);
      increment -= 33;
    }

    MpegVlcTables.MacroblockAddressIncrement.Write(writer, increment);
  }

  /// <summary>Writes one forward vector component as motion_code and its residual.</summary>
  /// <remarks>
  /// 7.6.3.1 reads <c>delta = (|motion_code| - 1) * f + motion_residual + 1</c>, signed by
  /// motion_code, with zero alone meaning no displacement; this inverts exactly that. The difference
  /// is folded by the range first, because the vector and the one it is predicted from both lie
  /// inside <c>[-16f, 16f)</c> while their difference need not, and the decoder folds the sum back.
  /// </remarks>
  private static void _WriteMotionCode(MpegBitWriter writer, int difference) {
    const int range = 2 * _MOTION_LIMIT;
    if (difference < -_MOTION_LIMIT)
      difference += range;
    else if (difference >= _MOTION_LIMIT)
      difference -= range;

    if (difference == 0) {
      MpegVlcTables.MotionCode.Write(writer, 0);
      return;
    }

    var magnitude = Math.Abs(difference) - 1;
    var code = magnitude / _MOTION_SCALE + 1;
    var residual = magnitude % _MOTION_SCALE;

    MpegVlcTables.MotionCode.Write(writer, difference < 0 ? -code : code);
    writer.WriteBits(residual, _FORWARD_F_CODE - 1);
  }

  private void _WriteSequenceHeader(MpegBitWriter writer) {
    writer.WriteStartCode(MpegStartCode.SequenceHeader);
    writer.WriteBits(this._width, 12);
    writer.WriteBits(this._height, 12);
    writer.WriteBits(1, 4);                    // aspect_ratio_information: square samples
    writer.WriteBits(this._frameRateCode, 4);
    writer.WriteBits(_BIT_RATE_VALUE, 18);     // 400 bit/s units
    writer.WriteBit(1);                        // marker_bit
    writer.WriteBits(_VBV_BUFFER_SIZE_VALUE, 10);
    writer.WriteBit(0);                        // constrained_parameters_flag, zero in MPEG-2
    writer.WriteBit(0);                        // load_intra_quantiser_matrix: use Table 7-4
    writer.WriteBit(0);                        // load_non_intra_quantiser_matrix: use Table 7-5
  }

  private static void _WriteSequenceExtension(MpegBitWriter writer) {
    writer.WriteStartCode(MpegStartCode.Extension);
    writer.WriteBits(1, 4);                    // extension_start_code_identifier: sequence_extension
    writer.WriteBits(0x48, 8);                 // Main Profile @ Main Level
    writer.WriteBit(1);                        // progressive_sequence
    writer.WriteBits(1, 2);                    // chroma_format: 4:2:0
    writer.WriteBits(0, 2);                    // horizontal_size_extension
    writer.WriteBits(0, 2);                    // vertical_size_extension
    writer.WriteBits(0, 12);                   // bit_rate_extension
    writer.WriteBit(1);                        // marker_bit
    writer.WriteBits(0, 8);                    // vbv_buffer_size_extension
    writer.WriteBit(0);                        // low_delay
    writer.WriteBits(0, 2);                    // frame_rate_extension_n
    writer.WriteBits(0, 5);                    // frame_rate_extension_d
  }

  private void _WritePictureHeader(MpegBitWriter writer, bool isIntra) {
    writer.WriteStartCode(MpegStartCode.Picture);
    writer.WriteBits((int)(this._pictureNumber & 0x3FF), 10); // temporal_reference
    writer.WriteBits(isIntra ? MpegPictureDecoder.IntraCoded : MpegPictureDecoder.PredictiveCoded, 3);
    writer.WriteBits(0xFFFF, 16);               // vbv_delay: unspecified for this VBR stream

    // MPEG-1's full_pel and f_code fields still exist in the H.262 picture header and are still read
    // by a decoder, but an MPEG-2 picture states its real f_codes in the coding extension below and
    // these are ignored. They are written as the "unused" pattern rather than left to chance.
    if (!isIntra) {
      writer.WriteBit(0);                      // full_pel_forward_vector
      writer.WriteBits(7, 3);                  // forward_f_code
    }

    writer.WriteBit(0);                        // extra_bit_picture
  }

  private static void _WritePictureCodingExtension(MpegBitWriter writer, bool isIntra) {
    writer.WriteStartCode(MpegStartCode.Extension);
    writer.WriteBits(8, 4);                    // picture_coding_extension

    // An I picture carries no vectors at all and says so with 6.3.10's f_code 15; a P picture states
    // the forward range it uses and leaves the backward pair unused, because it has no backward
    // reference to state one against.
    writer.WriteBits(isIntra ? _F_CODE_UNUSED : _FORWARD_F_CODE, 4); // f_code[0][0]
    writer.WriteBits(isIntra ? _F_CODE_UNUSED : _FORWARD_F_CODE, 4); // f_code[0][1]
    writer.WriteBits(_F_CODE_UNUSED, 4);       // f_code[1][0]
    writer.WriteBits(_F_CODE_UNUSED, 4);       // f_code[1][1]
    writer.WriteBits(0, 2);                    // intra_dc_precision: 8 bits
    writer.WriteBits(3, 2);                    // picture_structure: frame picture
    writer.WriteBit(0);                        // top_field_first
    writer.WriteBit(1);                        // frame_pred_frame_dct
    writer.WriteBit(0);                        // concealment_motion_vectors
    writer.WriteBit(0);                        // q_scale_type: linear
    writer.WriteBit(0);                        // intra_vlc_format: Table B.14
    writer.WriteBit(0);                        // alternate_scan: zig-zag
    writer.WriteBit(0);                        // repeat_first_field
    writer.WriteBit(1);                        // chroma_420_type
    writer.WriteBit(1);                        // progressive_frame
    writer.WriteBit(0);                        // composite_display_flag
  }

  /// <summary>
  /// Finds the whole-pixel vector whose 16x16 luminance prediction differs least from the source,
  /// and returns it in the half-sample units MPEG-2 codes vectors in.
  /// </summary>
  /// <remarks>
  /// The search is whole-pixel and the vector it returns is therefore always even. That is a
  /// deliberate restriction rather than an oversight: an even half-sample vector addresses a real
  /// sample, so the prediction the decoder forms is the one this compared against, with no
  /// interpolation between them to disagree about.
  /// <para/>
  /// It is centred on the predicted vector, because that is what the coded difference is measured
  /// from. The zero vector is the incumbent and is only displaced by a strictly better one: a
  /// macroblock that did not move has to come out of here with a zero vector or it cannot be
  /// skipped, and on flat or repeating content many vectors score identically. Taking the first
  /// equal-scoring candidate instead picks whichever corner the scan began at, and the picture then
  /// spends a type and two vectors per macroblock saying that nothing happened.
  /// </remarks>
  private (int X, int Y) _SearchMotion(
    MpegFrame source, MpegFrame reference, int macroblockX, int macroblockY, int predictedX, int predictedY) {
    var originX = macroblockX * 16;
    var originY = macroblockY * 16;

    var best = (X: 0, Y: 0);
    var bestCost = _MatchCost(source, reference, originX, originY, 0, 0, int.MaxValue);

    var centreX = predictedX / 2;
    var centreY = predictedY / 2;

    for (var candidateY = centreY - _SEARCH_RANGE; candidateY <= centreY + _SEARCH_RANGE; ++candidateY)
    for (var candidateX = centreX - _SEARCH_RANGE; candidateX <= centreX + _SEARCH_RANGE; ++candidateX) {
      // The reconstructed vector, not the coded difference, is what 7.6.3.1 folds into the range the
      // f_code states, so a candidate outside it would come back as a different vector entirely.
      if (2 * candidateX < -_MOTION_LIMIT || 2 * candidateX >= _MOTION_LIMIT
          || 2 * candidateY < -_MOTION_LIMIT || 2 * candidateY >= _MOTION_LIMIT)
        continue;

      // Neither standard permits a vector that reads outside the reference picture, and a decoder
      // that meets one refuses the stream. The cost function would happily score such a candidate,
      // because it repeats the edge sample rather than failing, so the search has to exclude it
      // here: an encoder whose prediction comes from samples the decoder will not read is writing a
      // picture nobody can decode.
      if (!_VectorFits(reference, originX, originY, 2 * candidateX, 2 * candidateY))
        continue;

      var cost = _MatchCost(source, reference, originX, originY, candidateX, candidateY, bestCost);
      if (cost >= bestCost)
        continue;

      bestCost = cost;
      best = (2 * candidateX, 2 * candidateY);
    }

    return best;
  }

  /// <summary>
  /// Absolute difference between a macroblock and the prediction one whole-pixel vector offers,
  /// abandoned as soon as it cannot beat <paramref name="ceiling"/>.
  /// </summary>
  private static int _MatchCost(
    MpegFrame source, MpegFrame reference, int originX, int originY, int vectorX, int vectorY, int ceiling) {
    var cost = 0;
    for (var y = 0; y < 16 && cost < ceiling; ++y)
    for (var x = 0; x < 16; ++x)
      cost += Math.Abs(
        _Sample(source.Luma, source.LumaWidth, source.LumaHeight, originX + x, originY + y)
        - _Sample(reference.Luma, reference.LumaWidth, reference.LumaHeight,
            originX + x + vectorX, originY + y + vectorY));

    return cost;
  }

  /// <summary>
  /// Quantises the six residual blocks of one motion-compensated macroblock into
  /// <paramref name="levels"/> and returns the coded block pattern.
  /// </summary>
  /// <remarks>
  /// The prediction is formed by the very routine the decoder predicts with, not by a copy of it.
  /// That matters most in chrominance: the luminance vector here is always an even number of
  /// half-samples, but 7.6.3.4 halves it for a 4:2:0 chrominance plane, so an odd luminance
  /// displacement lands chrominance between two samples and the decoder interpolates. An encoder
  /// that instead rounded to the nearer sample would compute its residual against a prediction its
  /// decoder never forms, and the error -- a colour fringe on moving edges -- would accumulate along
  /// the group while the luminance plane stayed clean.
  /// </remarks>
  private int _QuantiseResidual(
    MpegFrame source, MpegFrame reference, int macroblockX, int macroblockY,
    int vectorX, int vectorY, int quantiserScaleCode, int[] levels) {
    var quantiserScale = quantiserScaleCode * 2;
    var pattern = 0;

    Span<int> prediction = stackalloc int[64];
    Span<int> residual = stackalloc int[64];
    for (var block = 0; block < 6; ++block) {
      var (plane, planeWidth, originX, originY, component) = _BlockOf(source, macroblockX, macroblockY, block);
      var (referencePlane, referenceWidth, referenceHeight) = _PlaneOf(reference, component);
      var (blockVectorX, blockVectorY) = _VectorFor(component, vectorX, vectorY);

      if (!MpegMotionCompensation.TryPredict(
            prediction, 8, 0,
            referencePlane, referenceWidth, 0, referenceWidth, referenceHeight,
            originX, originY, 8, 8, blockVectorX, blockVectorY))
        throw new InvalidDataException(
          $"The motion search chose a vector of ({vectorX}, {vectorY}) half-samples for macroblock "
          + $"({macroblockX}, {macroblockY}), which reads outside the reference picture.");

      for (var index = 0; index < 64; ++index)
        residual[index] = plane[(originY + index / 8) * planeWidth + originX + index % 8] - prediction[index];

      if (MpegInterBlockEncoder.TryQuantise(residual, quantiserScale, isMpeg2: true, levels.AsSpan(block * 64, 64)))
        pattern |= 1 << (5 - block);
    }

    return pattern;
  }

  /// <summary>One sample of a plane, with the edge repeated past its bounds.</summary>
  /// <remarks>
  /// Only the search scores samples through this, and only ever inside the picture, because every
  /// candidate it scores has been checked to fit. The clamp is what makes that safe to rely on
  /// rather than something the scoring loop has to prove for itself.
  /// </remarks>
  private static int _Sample(byte[] plane, int width, int height, int x, int y)
    => plane[Math.Clamp(y, 0, height - 1) * width + Math.Clamp(x, 0, width - 1)];

  /// <summary>Whether every one of a macroblock's six predictions stays inside the reference.</summary>
  /// <remarks>
  /// Chrominance is checked as well as luminance and is not implied by it: halving the vector can
  /// leave a half-sample step, and an interpolated prediction reaches one sample further than a
  /// copied one does.
  /// </remarks>
  private static bool _VectorFits(MpegFrame reference, int originX, int originY, int vectorX, int vectorY) {
    Span<int> prediction = stackalloc int[256];

    for (var component = 0; component < 3; ++component) {
      var (plane, width, height) = _PlaneOf(reference, component);
      var (blockVectorX, blockVectorY) = _VectorFor(component, vectorX, vectorY);
      var size = component == 0 ? 16 : 8;
      var x = component == 0 ? originX : originX / 2;
      var y = component == 0 ? originY : originY / 2;

      if (!MpegMotionCompensation.TryPredict(
            prediction, size, 0, plane, width, 0, width, height, x, y, size, size, blockVectorX, blockVectorY))
        return false;
    }

    return true;
  }

  private static (byte[] Plane, int Width, int Height) _PlaneOf(MpegFrame frame, int component) => component switch {
    0 => (frame.Luma, frame.LumaWidth, frame.LumaHeight),
    1 => (frame.Cb, frame.ChromaWidth, frame.ChromaHeight),
    _ => (frame.Cr, frame.ChromaWidth, frame.ChromaHeight),
  };

  /// <summary>
  /// The vector for one plane, in that plane's own half-sample units (13818-2, 7.6.3.4).
  /// </summary>
  /// <remarks>
  /// The halving for 4:2:0 chrominance truncates towards zero, which is what the standard states and
  /// is <em>not</em> the arithmetic shift that halves the whole-sample part of a vector. The two
  /// disagree for every negative odd vector, and using one for both puts chrominance half a sample
  /// out on everything moving up or left.
  /// </remarks>
  private static (int X, int Y) _VectorFor(int component, int vectorX, int vectorY)
    => component == 0 ? (vectorX, vectorY) : (vectorX / 2, vectorY / 2);

  private void _WriteIntraBlock(
    MpegBitWriter writer, MpegFrame source, int macroblockX, int macroblockY, int blockIndex,
    int quantiserScaleCode, int[] dcPredictor) {
    var (plane, planeWidth, originX, originY, component) = _BlockOf(source, macroblockX, macroblockY, blockIndex);

    Span<int> samples = stackalloc int[64];
    for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x)
        samples[y * 8 + x] = plane[(originY + y) * planeWidth + originX + x];

    Span<double> transformed = stackalloc double[64];
    MpegForwardDct.Transform(samples, transformed);

    var dc = Math.Clamp((int)Math.Round(transformed[0] / 8d, MidpointRounding.AwayFromZero), 0, 255);
    var differential = dc - dcPredictor[component];
    dcPredictor[component] = dc;
    _WriteDc(writer, component != 0, differential);

    var quantiserScale = quantiserScaleCode * 2;
    var run = 0;
    for (var scan = 1; scan < 64; ++scan) {
      var raster = MpegQuantisation.ZigZagScan[scan];
      var weight = MpegQuantisation.DefaultIntraMatrix[raster];
      var level = (int)Math.Round(transformed[raster] * 16d / (weight * quantiserScale), MidpointRounding.AwayFromZero);
      level = Math.Clamp(level, -2047, 2047);
      if (level == 0) {
        ++run;
        continue;
      }

      _WriteCoefficient(writer, run, level);
      run = 0;
    }

    MpegVlcTables.Coefficient.Write(writer, MpegVlcTables.EndOfBlock);
  }

  private static void _WriteDc(MpegBitWriter writer, bool isChroma, int differential) {
    var size = _MagnitudeBits(differential);
    (isChroma ? MpegVlcTables.Mpeg2ChrominanceDcSize : MpegVlcTables.Mpeg2LuminanceDcSize).Write(writer, size);
    if (size == 0)
      return;

    var value = differential >= 0 ? differential : differential + (1 << size) - 1;
    writer.WriteBits(value, size);
  }

  private static void _WriteCoefficient(MpegBitWriter writer, int run, int level) {
    var packed = (run << 8) | Math.Abs(level);
    if (MpegVlcTables.Coefficient.TryWrite(writer, packed)) {
      writer.WriteBit(level < 0 ? 1 : 0);
      return;
    }

    MpegVlcTables.Coefficient.Write(writer, MpegVlcTables.CoefficientEscape);
    writer.WriteBits(run, 6);
    writer.WriteBits((uint)(level & 0xFFF), 12);
  }

  private MpegFrame _ToFrame(RawImage frame) {
    if (!frame.HasEnoughPixelData)
      throw new InvalidDataException("The source RawImage does not contain enough pixel data for its declared format and dimensions.");

    var chromaWidth = this._width >> 1;
    var chromaHeight = this._height >> 1;
    var packed = RawYuvPlanes.Subsampled(frame, PixelFormat.Yuv420P8, chromaWidth, chromaHeight);
    var target = new MpegFrame(this._codedWidth, this._codedHeight, MpegChromaFormat.Yuv420);

    var lumaSamples = this._width * this._height;
    var chromaSamples = chromaWidth * chromaHeight;
    _PadPlane(packed.AsSpan(0, lumaSamples), this._width, this._height, target.Luma, target.LumaWidth, target.LumaHeight);
    _PadPlane(packed.AsSpan(lumaSamples, chromaSamples), chromaWidth, chromaHeight, target.Cb, target.ChromaWidth, target.ChromaHeight);
    _PadPlane(packed.AsSpan(lumaSamples + chromaSamples, chromaSamples), chromaWidth, chromaHeight, target.Cr, target.ChromaWidth, target.ChromaHeight);

    return target;
  }

  private static void _PadPlane(
    ReadOnlySpan<byte> source, int sourceWidth, int sourceHeight,
    Span<byte> target, int targetWidth, int targetHeight) {
    for (var y = 0; y < targetHeight; ++y) {
      var sourceRow = Math.Min(y, sourceHeight - 1);
      var sourceAt = sourceRow * sourceWidth;
      var targetAt = y * targetWidth;
      source.Slice(sourceAt, sourceWidth).CopyTo(target[targetAt..]);
      target.Slice(targetAt + sourceWidth, targetWidth - sourceWidth).Fill(source[sourceAt + sourceWidth - 1]);
    }
  }

  private static (byte[] Plane, int Width, int X, int Y, int Component) _BlockOf(
    MpegFrame frame, int macroblockX, int macroblockY, int blockIndex) {
    if (blockIndex < 4)
      return (
        frame.Luma,
        frame.LumaWidth,
        macroblockX * 16 + (blockIndex & 1) * 8,
        macroblockY * 16 + (blockIndex >> 1) * 8,
        0);

    return blockIndex == 4
      ? (frame.Cb, frame.ChromaWidth, macroblockX * 8, macroblockY * 8, 1)
      : (frame.Cr, frame.ChromaWidth, macroblockX * 8, macroblockY * 8, 2);
  }

  private static int _MagnitudeBits(int value) {
    var magnitude = Math.Abs(value);
    var bits = 0;
    while (magnitude != 0) {
      ++bits;
      magnitude >>= 1;
    }

    return bits;
  }

  private static bool _FitsMainLevel(int byteCount, Rational frameRate)
    => (Int128)byteCount * 8 * frameRate.Numerator <= (Int128)_MAX_BIT_RATE * frameRate.Denominator;

  private static (int Code, Rational Rate) _FrameRateOf(Rational requested) {
    if (!requested.IsKnown)
      return (3, new Rational(25, 1));

    foreach (var candidate in new (int Code, Rational Rate)[] {
               (1, new Rational(24_000, 1_001)),
               (2, new Rational(24, 1)),
               (3, new Rational(25, 1)),
               (4, new Rational(30_000, 1_001)),
               (5, new Rational(30, 1)),
             })
      if ((Int128)requested.Numerator * candidate.Rate.Denominator == (Int128)candidate.Rate.Numerator * requested.Denominator)
        return candidate;

    throw new NotSupportedException(
      $"This Main-Level MPEG-2 encoder writes the base frame rates 24000/1001, 24, 25, 30000/1001 and 30 fps; "
      + $"{requested} was requested.");
  }
}
