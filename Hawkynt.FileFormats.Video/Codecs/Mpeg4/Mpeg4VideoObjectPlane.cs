using System;
using System.IO;

namespace FileFormat.Codecs.Mpeg4;

/// <summary>
/// The video object plane header of ISO/IEC 14496-2, 6.2.5: one coded picture's own parameters.
/// </summary>
internal sealed class Mpeg4VideoObjectPlane {

  /// <summary>Intra coded: decodable on its own.</summary>
  internal const int IntraCoded = 0;

  /// <summary>Predictively coded, from the picture before it.</summary>
  internal const int PredictiveCoded = 1;

  /// <summary>Bidirectionally coded, from the pictures on either side of it.</summary>
  internal const int BidirectionallyCoded = 2;

  /// <summary>Sprite coded, which needs the sprite the video object layer refuses.</summary>
  internal const int SpriteCoded = 3;

  /// <summary>Which of I, P, B, S this picture is.</summary>
  internal required int CodingType { get; init; }

  /// <summary>
  /// Whether the picture carries any macroblocks at all.
  /// </summary>
  /// <remarks>
  /// A picture with <c>vop_coded</c> clear is the previous picture again and carries nothing after
  /// the header. It exists so that a constant-frame-rate stream can spend almost nothing on a moment
  /// where nothing happened, and a decoder that skipped it rather than showing it would hand back
  /// fewer frames than the stream has.
  /// </remarks>
  internal required bool IsCoded { get; init; }

  /// <summary>
  /// Which way the half-sample interpolation rounds (ISO/IEC 14496-2, 7.6.2.1).
  /// </summary>
  /// <remarks>
  /// One means round down and zero means round up, and an encoder alternates it between pictures so
  /// that the bias of the interpolation does not accumulate in one direction through a long run of
  /// predicted pictures. Fixing it at either value leaves a decode that drifts brighter or darker
  /// across a group of pictures — slowly enough to look like the film rather than like a bug.
  /// </remarks>
  internal required int RoundingType { get; init; }

  /// <summary>
  /// The quantiser above which an intra block's DC is coded as an ordinary coefficient rather than
  /// with the DC tables (ISO/IEC 14496-2, Table 6-27).
  /// </summary>
  internal required int IntraDcThreshold { get; init; }

  /// <summary>The quantiser this picture starts at.</summary>
  internal required int Quantiser { get; init; }

  /// <summary>The scaling the forward motion vectors are coded with; 1 is half-sample resolution.</summary>
  internal required int ForwardFCode { get; init; }

  /// <summary>The scaling the backward motion vectors are coded with.</summary>
  internal required int BackwardFCode { get; init; }

  /// <summary>
  /// How many whole seconds this picture is past the one its <c>modulo_time_base</c> counts from.
  /// </summary>
  /// <remarks>
  /// Which picture that is depends on this one's type, and the header cannot say — so the count is
  /// handed out raw and the decoder, which knows what it has decoded, turns the two into a time.
  /// ISO/IEC 14496-2 6.3.5 counts an intra or predicted picture's seconds from the previously
  /// decoded intra or predicted picture, <i>in decoding order</i>, and a bidirectionally coded
  /// picture's from the one before it <i>in display order</i> — which is a different picture, because
  /// the anchor a bidirectional picture is shown before has already been decoded by the time it
  /// arrives.
  /// </remarks>
  internal required int ModuloSeconds { get; init; }

  /// <summary>This picture's time within its second, in ticks of the layer's resolution.</summary>
  internal required int TimeIncrement { get; init; }

  /// <summary>
  /// Reads a video object plane header, positioned just past its start code.
  /// </summary>
  /// <param name="reader">The bitstream.</param>
  /// <param name="layer">The video object layer in force, whose fields size several of these.</param>
  internal static Mpeg4VideoObjectPlane Parse(ref Mpeg4BitReader reader, Mpeg4VideoObjectLayer layer) {
    ArgumentNullException.ThrowIfNull(layer);

    var codingType = reader.ReadBits(2);
    if (codingType == SpriteCoded)
      throw new NotSupportedException(
        "This MPEG-4 picture is sprite coded (ISO/IEC 14496-2 6.3.5), which reads its samples out of a warped "
        + "reference rather than predicting them. Sprites are not implemented.");

    // modulo_time_base: one bit per whole second since the last one, ended by a zero.
    var seconds = 0;
    while (reader.ReadBit() == 1) {
      ++seconds;
      if (seconds > 1 << 16)
        throw new InvalidDataException(
          "The modulo_time_base of this MPEG-4 picture header is a run of more than sixty-five thousand ones, which "
          + "means the header is being read at the wrong bit position rather than that the picture is that far away "
          + "in time.");
    }

    reader.ReadMarkerBit("before vop_time_increment");
    var timeIncrement = reader.ReadBits(layer.TimeIncrementBits);
    reader.ReadMarkerBit("after vop_time_increment");

    var isCoded = reader.ReadBit() == 1;
    if (!isCoded)
      return new() {
        CodingType = codingType,
        IsCoded = false,
        RoundingType = 0,
        IntraDcThreshold = 0,
        Quantiser = 1,
        ForwardFCode = 1,
        BackwardFCode = 1,
        ModuloSeconds = seconds,
        TimeIncrement = timeIncrement,
      };

    var roundingType = codingType == PredictiveCoded ? reader.ReadBit() : 0;
    var intraDcThreshold = reader.ReadBits(3);
    var quantiser = reader.ReadBits(layer.QuantiserPrecision);
    if (quantiser == 0)
      throw new InvalidDataException(
        "This MPEG-4 picture states vop_quant 0, which ISO/IEC 14496-2 6.3.5 forbids; zero is not a step size and "
        + "would reconstruct every coefficient as zero.");

    var forwardFCode = 1;
    var backwardFCode = 1;
    if (codingType != IntraCoded)
      forwardFCode = _ReadFCode(ref reader, "vop_fcode_forward");

    if (codingType == BidirectionallyCoded)
      backwardFCode = _ReadFCode(ref reader, "vop_fcode_backward");

    return new() {
      CodingType = codingType,
      IsCoded = true,
      RoundingType = roundingType,
      IntraDcThreshold = intraDcThreshold,
      Quantiser = quantiser,
      ForwardFCode = forwardFCode,
      BackwardFCode = backwardFCode,
      ModuloSeconds = seconds,
      TimeIncrement = timeIncrement,
    };
  }

  private static int _ReadFCode(ref Mpeg4BitReader reader, string field) {
    var fCode = reader.ReadBits(3);
    if (fCode == 0)
      throw new InvalidDataException(
        $"This MPEG-4 picture states {field} 0, which ISO/IEC 14496-2 6.3.5 forbids; the range is 1 to 7.");

    return fCode;
  }
}
