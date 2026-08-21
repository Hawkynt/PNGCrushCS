using System;
using System.IO;

namespace FileFormat.Codecs.Mpeg4;

/// <summary>
/// The video object layer header of ISO/IEC 14496-2, 6.2.3: everything that is true of every picture
/// until the next one arrives.
/// </summary>
/// <remarks>
/// This is where a stream says which of the coding tools it uses, and it is therefore where almost
/// every refusal in this decoder lives. A tool that is signalled here and not implemented has to be
/// refused here rather than where its bits appear, because by the time its bits appear the decoder
/// has already read the ones before them as something else.
/// <para/>
/// A stream may carry the header again, which is how a decoder is able to start part way into a
/// broadcast, and repeats normally restate the same values.
/// </remarks>
internal sealed class Mpeg4VideoObjectLayer {

  /// <summary>Rectangular, which is the only shape a stream in a file uses (Table 6-15).</summary>
  private const int _RECTANGULAR = 0;

  /// <summary>The chroma_format value that means 4:2:0 (Table 6-13).</summary>
  private const int _CHROMA_420 = 1;

  /// <summary>The picture's width in pixels, as displayed.</summary>
  internal required int Width { get; init; }

  /// <summary>The picture's height in pixels, as displayed.</summary>
  internal required int Height { get; init; }

  /// <summary>Macroblocks across, which is the width rounded up to a multiple of sixteen, over sixteen.</summary>
  internal int MacroblockWidth => (this.Width + 15) / 16;

  /// <summary>Macroblocks down.</summary>
  internal int MacroblockHeight => (this.Height + 15) / 16;

  /// <summary>
  /// Which of the two inverse quantisation methods of clause 7.4.4 the layer uses.
  /// </summary>
  /// <remarks>
  /// False is the H.263 method — one step size for the whole block and a formula — and true is the
  /// MPEG method, which weights every coefficient by a position in a matrix. They are not close
  /// approximations of each other: reading a stream coded with one as though it used the other gives
  /// a picture whose detail is at the wrong contrast everywhere, which looks like a decode.
  /// </remarks>
  internal required bool UsesMpegQuantisation { get; init; }

  /// <summary>The intra weighting matrix in raster order, used only by the MPEG method.</summary>
  internal required byte[] IntraQuantiserMatrix { get; init; }

  /// <summary>The non-intra weighting matrix in raster order.</summary>
  internal required byte[] NonIntraQuantiserMatrix { get; init; }

  /// <summary>Whether motion vectors are coded to a quarter of a sample rather than to a half.</summary>
  internal required bool QuarterSample { get; init; }

  /// <summary>How many bits <c>vop_time_increment</c> occupies in every picture header.</summary>
  internal required int TimeIncrementBits { get; init; }

  /// <summary>
  /// How many ticks of <c>vop_time_increment</c> make one second.
  /// </summary>
  /// <remarks>
  /// Not the same as two to the power of <see cref="TimeIncrementBits"/>, and the difference matters:
  /// a stream at twenty-five ticks a second spends five bits on the increment, and using thirty-two
  /// as the second in place of twenty-five would put every picture that crosses a second boundary at
  /// the wrong distance from its neighbours — which is exactly the distance the direct prediction
  /// mode of a bidirectionally coded picture scales its vectors by.
  /// </remarks>
  internal required int TimeIncrementResolution { get; init; }

  /// <summary>How many bits <c>vop_quant</c> occupies.</summary>
  internal required int QuantiserPrecision { get; init; }

  /// <summary>Whether the layer may carry resync markers inside a picture.</summary>
  internal required bool MayCarryResyncMarkers { get; init; }

  /// <summary>Whether another header describes the same picture geometry as this one.</summary>
  internal bool SameGeometryAs(Mpeg4VideoObjectLayer other) {
    ArgumentNullException.ThrowIfNull(other);

    return this.Width == other.Width && this.Height == other.Height;
  }

  /// <summary>
  /// Reads a video object layer header, positioned just past its start code.
  /// </summary>
  internal static Mpeg4VideoObjectLayer Parse(ref Mpeg4BitReader reader) {
    reader.ReadBit(); // random_accessible_vol: whether every picture is intra coded, which changes nothing here.
    var objectType = reader.ReadBits(8);
    if (objectType == 0)
      throw new InvalidDataException(
        "This MPEG-4 video object layer states video_object_type_indication 0, which ISO/IEC 14496-2 Table 6-14 "
        + "leaves reserved.");

    var verid = 1;
    if (reader.ReadBit() == 1) {
      verid = reader.ReadBits(4);
      reader.ReadBits(3); // video_object_layer_priority
    }

    if (reader.ReadBits(4) == 15)
      reader.ReadBits(16); // extended pixel aspect ratio: display geometry, which changes no sample.

    if (reader.ReadBit() == 1) {
      var chromaFormat = reader.ReadBits(2);
      if (chromaFormat != _CHROMA_420)
        throw new NotSupportedException(
          $"This MPEG-4 video object layer states chroma_format {chromaFormat}. ISO/IEC 14496-2 Table 6-13 defines "
          + "only 1 (4:2:0) for this part of the standard, and nothing else is implemented.");

      // low_delay: whether the stream promises that no picture is ever reordered. Read and not kept,
      // because the reordering this decoder does follows from the picture types alone — a stream that
      // set the flag and then carried a bidirectionally coded picture would be handled correctly and
      // a stream that cleared it and carried none would cost nothing.
      reader.ReadBit();

      if (reader.ReadBit() == 1)
        _SkipVideoBufferingVerifier(ref reader);
    }

    var shape = reader.ReadBits(2);
    if (shape != _RECTANGULAR)
      throw new NotSupportedException(
        $"This MPEG-4 video object layer states video_object_layer_shape {shape}, which is a shaped object rather "
        + "than a rectangular picture (ISO/IEC 14496-2 Table 6-15). Binary and grayscale shape coding are not "
        + "implemented.");

    reader.ReadMarkerBit("before vop_time_increment_resolution");
    var timeIncrementResolution = reader.ReadBits(16);
    if (timeIncrementResolution == 0)
      throw new InvalidDataException(
        "This MPEG-4 video object layer states vop_time_increment_resolution 0, which ISO/IEC 14496-2 6.3.3 forbids.");

    reader.ReadMarkerBit("after vop_time_increment_resolution");

    var timeIncrementBits = _BitsFor(timeIncrementResolution);
    if (reader.ReadBit() == 1)
      reader.ReadBits(timeIncrementBits); // fixed_vop_time_increment

    reader.ReadMarkerBit("before video_object_layer_width");
    var width = reader.ReadBits(13);
    reader.ReadMarkerBit("after video_object_layer_width");
    var height = reader.ReadBits(13);
    reader.ReadMarkerBit("after video_object_layer_height");

    if (width == 0 || height == 0)
      throw new InvalidDataException(
        $"This MPEG-4 video object layer states a picture size of {width}x{height}, and neither dimension may be zero.");

    if (reader.ReadBit() == 1)
      throw new NotSupportedException(
        "This MPEG-4 video object layer is interlaced (ISO/IEC 14496-2 6.3.3). Field-coded macroblocks, the field "
        + "motion vectors that go with them and the alternate scan are not implemented.");

    if (reader.ReadBit() != 1)
      throw new NotSupportedException(
        "This MPEG-4 video object layer clears obmc_disable, asking for overlapped block motion compensation "
        + "(ISO/IEC 14496-2 7.6.4). That is not implemented.");

    var spriteEnable = verid == 1 ? reader.ReadBits(1) : reader.ReadBits(2);
    if (spriteEnable != 0)
      throw new NotSupportedException(
        $"This MPEG-4 video object layer states sprite_enable {spriteEnable}: "
        + (spriteEnable == 2
          ? "global motion compensation, which warps the whole reference picture by up to three points before "
            + "predicting from it (ISO/IEC 14496-2 7.8)."
          : "a static sprite, a background transmitted once and read out of by warping (ISO/IEC 14496-2 7.8).")
        + " Neither is implemented.");

    var quantiserPrecision = 5;
    if (reader.ReadBit() == 1) {
      quantiserPrecision = reader.ReadBits(4);
      var bitsPerPixel = reader.ReadBits(4);
      if (bitsPerPixel != 8)
        throw new NotSupportedException(
          $"This MPEG-4 video object layer states {bitsPerPixel}-bit samples (ISO/IEC 14496-2 6.3.3). Samples of any "
          + "depth but eight bits are not implemented.");

      if (quantiserPrecision is < 3 or > 9)
        throw new InvalidDataException(
          $"This MPEG-4 video object layer states quant_precision {quantiserPrecision}, outside the 3 to 9 that "
          + "ISO/IEC 14496-2 6.3.3 permits.");
    }

    var usesMpegQuantisation = reader.ReadBit() == 1;
    var intraMatrix = Mpeg4Quantisation.DefaultIntraMatrix;
    var nonIntraMatrix = Mpeg4Quantisation.DefaultNonIntraMatrix;
    if (usesMpegQuantisation) {
      if (reader.ReadBit() == 1)
        intraMatrix = _ReadQuantiserMatrix(ref reader, Mpeg4Quantisation.DefaultIntraMatrix);

      if (reader.ReadBit() == 1)
        nonIntraMatrix = _ReadQuantiserMatrix(ref reader, Mpeg4Quantisation.DefaultNonIntraMatrix);
    }

    var quarterSample = verid != 1 && reader.ReadBit() == 1;
    if (quarterSample)
      throw new NotSupportedException(
        "This MPEG-4 video object layer states quarter_sample (ISO/IEC 14496-2 7.6.2.2): its luminance motion "
        + "vectors are to a quarter of a sample, interpolated with an eight-tap filter over a block extended by "
        + "mirroring rather than with the bilinear filter of 7.6.2.1. That is not implemented.");

    if (reader.ReadBit() != 1)
      throw new NotSupportedException(
        "This MPEG-4 video object layer clears complexity_estimation_disable, so every picture header carries a "
        + "complexity estimation header whose length depends on flags defined here (ISO/IEC 14496-2 6.3.3). That is "
        + "not implemented.");

    var mayCarryResyncMarkers = reader.ReadBit() == 0;

    if (reader.ReadBit() == 1)
      throw new NotSupportedException(
        "This MPEG-4 video object layer is data partitioned (ISO/IEC 14496-2 6.3.5.2), which splits each video "
        + "packet's motion data from its coefficient data and separates them with a marker. That is not implemented.");

    if (verid != 1) {
      if (reader.ReadBit() == 1)
        throw new NotSupportedException(
          "This MPEG-4 video object layer enables newpred (ISO/IEC 14496-2 6.3.3), in which a picture states which "
          + "earlier picture it predicts from. That is not implemented.");

      if (reader.ReadBit() == 1)
        throw new NotSupportedException(
          "This MPEG-4 video object layer enables reduced resolution pictures (ISO/IEC 14496-2 6.3.3), which are "
          + "coded at half size and scaled up before prediction. That is not implemented.");
    }

    if (reader.ReadBit() == 1)
      throw new NotSupportedException(
        "This MPEG-4 video object layer is scalable (ISO/IEC 14496-2 6.3.3): it is one layer of a stream whose "
        + "pictures are built from several. Scalability is not implemented.");

    return new() {
      Width = width,
      Height = height,
      UsesMpegQuantisation = usesMpegQuantisation,
      IntraQuantiserMatrix = intraMatrix,
      NonIntraQuantiserMatrix = nonIntraMatrix,
      QuarterSample = quarterSample,
      TimeIncrementBits = timeIncrementBits,
      TimeIncrementResolution = timeIncrementResolution,
      QuantiserPrecision = quantiserPrecision,
      MayCarryResyncMarkers = mayCarryResyncMarkers,
    };
  }

  /// <summary>
  /// Steps over the video buffering verifier parameters, whose marker bits are checked on the way.
  /// </summary>
  /// <remarks>
  /// Nothing in them changes a sample — they describe how fast the bits arrive and how much buffer a
  /// decoder needs — but they are seventy-nine bits with five markers in them, and a decoder that
  /// skipped a fixed count without checking the markers would carry a mis-parse silently into the
  /// picture size.
  /// </remarks>
  private static void _SkipVideoBufferingVerifier(ref Mpeg4BitReader reader) {
    reader.ReadBits(15);
    reader.ReadMarkerBit("in first_half_bit_rate");
    reader.ReadBits(15);
    reader.ReadMarkerBit("in latter_half_bit_rate");
    reader.ReadBits(15);
    reader.ReadMarkerBit("in first_half_vbv_buffer_size");
    reader.ReadBits(3);
    reader.ReadBits(11);
    reader.ReadMarkerBit("in first_half_vbv_occupancy");
    reader.ReadBits(15);
    reader.ReadMarkerBit("in latter_half_vbv_occupancy");
  }

  /// <summary>
  /// Reads a loaded quantiser weighting matrix and un-zig-zags it (ISO/IEC 14496-2, 6.3.3).
  /// </summary>
  /// <remarks>
  /// The matrix is transmitted in the zig-zag scan order the coefficients are, and is held here in
  /// raster order so that dequantisation indexes it the same way it indexes the block. A weight of
  /// zero ends the list early and every remaining position keeps the value before it, which is the
  /// standard's way of writing a matrix that is constant from some frequency upward.
  /// </remarks>
  private static byte[] _ReadQuantiserMatrix(ref Mpeg4BitReader reader, byte[] fallback) {
    var matrix = new byte[64];
    var last = 0;

    for (var scan = 0; scan < 64; ++scan) {
      var weight = last;
      if (last != 0 || scan == 0) {
        weight = reader.ReadBits(8);
        if (weight == 0) {
          if (scan == 0)
            return fallback;

          weight = last;
        } else {
          last = weight;
        }
      }

      matrix[Mpeg4Quantisation.ZigZag[scan]] = (byte)weight;
    }

    return matrix;
  }

  /// <summary>
  /// How many bits a value in <c>[0, resolution)</c> needs, which is what
  /// <c>vop_time_increment</c> occupies.
  /// </summary>
  /// <remarks>
  /// At least one bit even for a resolution of one, because the field is present in every picture
  /// header and a field of no bits would leave the header one bit short of where it really ends.
  /// </remarks>
  private static int _BitsFor(int resolution) {
    var bits = 1;
    while (1 << bits < resolution)
      ++bits;

    return bits;
  }
}
