using System;
using System.IO;

namespace FileFormat.Codecs.Mpeg;

/// <summary>
/// The sequence header of ISO/IEC 11172-2, 2.4.2.3 and ISO/IEC 13818-2, 6.2.2.1 — everything that is
/// true of every picture until the next one arrives — together with the MPEG-2 extensions that
/// amend it.
/// </summary>
/// <remarks>
/// A stream may carry the header again before any group of pictures, which is how a decoder is able
/// to start part way into a broadcast. Repeats normally restate the same values, but they are
/// allowed to load new quantiser matrices, so this is re-read every time rather than parsed once and
/// assumed.
/// <para/>
/// The two standards share the header byte for byte; MPEG-2 then follows it with a sequence
/// extension that widens three of its fields and adds the rest. That is why this is amended in place
/// rather than parsed as two separate shapes: which standard a stream is only becomes known one
/// start code after the point where it matters, and a decoder that had already committed to MPEG-1
/// would have to unpick it.
/// </remarks>
internal sealed class MpegSequenceHeader {

  /// <summary>The picture's width in pixels, as displayed.</summary>
  internal int Width { get; private set; }

  /// <summary>The picture's height in pixels, as displayed.</summary>
  internal int Height { get; private set; }

  /// <summary>Macroblocks across, which is the width rounded up to a multiple of sixteen, over sixteen.</summary>
  internal int MacroblockWidth => (this.Width + 15) / 16;

  /// <summary>
  /// Macroblocks down (ISO/IEC 13818-2, 6.3.3).
  /// </summary>
  /// <remarks>
  /// Rounded up to a multiple of sixteen, except in an interlaced sequence, where it is rounded up
  /// to a multiple of thirty-two and then counted in sixteens. A frame of an interlaced sequence is
  /// two fields, each of which has to be a whole number of macroblock rows of its own, so a frame
  /// has to be an even number of them — a 48-line interlaced sequence is coded as four macroblock
  /// rows and not three, and the fourth is real: it is transmitted, and later pictures may predict
  /// from it. Rounding it to three makes the very first slice of such a stream state a row the
  /// picture does not have.
  /// </remarks>
  internal int MacroblockHeight => this.ProgressiveSequence
    ? (this.Height + 15) / 16
    : 2 * ((this.Height + 31) / 32);

  /// <summary>The intra quantiser weighting matrix in force, in raster order.</summary>
  internal byte[] IntraMatrix { get; private set; } = MpegQuantisation.DefaultIntraMatrix;

  /// <summary>The non-intra quantiser weighting matrix in force, in raster order.</summary>
  internal byte[] NonIntraMatrix { get; private set; } = MpegQuantisation.DefaultNonIntraMatrix;

  /// <summary>
  /// The intra matrix chrominance blocks are weighted by, which is the luminance one unless a
  /// quant matrix extension loaded a separate one (13818-2, 6.2.3.2).
  /// </summary>
  /// <remarks>
  /// Separate chrominance matrices exist only for 4:2:2 and 4:4:4; in 4:2:0 the standard requires
  /// the same matrix to be used for both, and a stream that loaded one anyway would be describing
  /// weights nothing applies. Holding them as separate fields that happen to be equal keeps the
  /// dequantisation from having to know which format it is in.
  /// </remarks>
  internal byte[] ChromaIntraMatrix { get; private set; } = MpegQuantisation.DefaultIntraMatrix;

  /// <summary>The non-intra matrix chrominance blocks are weighted by.</summary>
  internal byte[] ChromaNonIntraMatrix { get; private set; } = MpegQuantisation.DefaultNonIntraMatrix;

  /// <summary>Whether a sequence extension has been read, which is what makes this MPEG-2.</summary>
  internal bool IsMpeg2 { get; private set; }

  /// <summary>How many chrominance samples the pictures carry (13818-2, 6.3.5).</summary>
  internal MpegChromaFormat ChromaFormat { get; private set; } = MpegChromaFormat.Yuv420;

  /// <summary>progressive_sequence: every picture is a frame and none is interlaced (13818-2, 6.3.5).</summary>
  internal bool ProgressiveSequence { get; private set; } = true;

  /// <summary>How many 8x8 blocks a macroblock holds, which the chrominance format decides.</summary>
  internal int BlockCount => this.ChromaFormat switch {
    MpegChromaFormat.Yuv420 => 6,
    MpegChromaFormat.Yuv422 => 8,
    _ => 12,
  };

  /// <summary>Reads a sequence header, positioned just past its start code.</summary>
  /// <param name="reader">The bitstream.</param>
  /// <param name="previous">The header in force before this one, whose matrices carry over when this
  /// one loads neither.</param>
  internal static MpegSequenceHeader Parse(ref MpegBitReader reader, MpegSequenceHeader? previous) {
    var width = reader.ReadBits(12);
    var height = reader.ReadBits(12);
    if (width == 0 || height == 0)
      throw new InvalidDataException(
        $"The MPEG sequence header states a picture size of {width}x{height}, and neither dimension may be zero.");

    // pel_aspect_ratio and picture_rate are display geometry and display timing; neither changes a
    // sample, and there is nowhere in a RawImage to put either, so they are read to be stepped over.
    // picture_rate is still checked, because a value outside the eight the standard defines means
    // this is not a sequence header — and reading on from one that is not is how a decoder produces
    // a picture of noise instead of a refusal.
    reader.ReadBits(4);
    var pictureRate = reader.ReadBits(4);
    if (pictureRate is 0 or > 8)
      throw new InvalidDataException(
        $"The MPEG sequence header states frame_rate_code {pictureRate}, which ISO/IEC 11172-2 Table 2-C.4 and "
        + "ISO/IEC 13818-2 Table 6-4 leave forbidden or reserved. Rates 1 to 8 are the ones both standards define.");

    reader.ReadBits(18); // bit_rate
    if (reader.ReadBit() != 1)
      throw new InvalidDataException(
        "The marker bit in the MPEG sequence header is zero, so this is not a sequence header or the stream is corrupt.");

    reader.ReadBits(10); // vbv_buffer_size
    reader.ReadBit();    // constrained_parameters_flag

    // The matrices are transmitted in the zig-zag scan order the coefficients are, and are held here
    // in raster order so that dequantisation indexes them the same way it indexes the block.
    var intra = reader.ReadBit() == 1
      ? _ReadMatrix(ref reader)
      : previous?.IntraMatrix ?? MpegQuantisation.DefaultIntraMatrix;

    var nonIntra = reader.ReadBit() == 1
      ? _ReadMatrix(ref reader)
      : previous?.NonIntraMatrix ?? MpegQuantisation.DefaultNonIntraMatrix;

    return new() {
      Width = width,
      Height = height,
      IntraMatrix = intra,
      NonIntraMatrix = nonIntra,
      ChromaIntraMatrix = intra,
      ChromaNonIntraMatrix = nonIntra,
    };
  }

  /// <summary>
  /// Applies the sequence extension that follows a sequence header in an MPEG-2 stream
  /// (ISO/IEC 13818-2, 6.2.2.3), positioned just past its four-bit identifier.
  /// </summary>
  /// <remarks>
  /// This is the point at which a stream stops being MPEG-1. The extension widens the picture size
  /// by two bits in each direction, which is how MPEG-2 reaches past 4095 samples, and states the
  /// chrominance format and whether the sequence is progressive — the three things that change what
  /// the rest of the decode does.
  /// </remarks>
  internal void ApplySequenceExtension(ref MpegBitReader reader) {
    reader.ReadBits(8); // profile_and_level_indication
    this.ProgressiveSequence = reader.ReadBit() == 1;

    var chromaFormat = reader.ReadBits(2);
    this.ChromaFormat = chromaFormat switch {
      1 => MpegChromaFormat.Yuv420,
      2 => MpegChromaFormat.Yuv422,
      3 => MpegChromaFormat.Yuv444,
      _ => throw new InvalidDataException(
        "The MPEG-2 sequence extension states chroma_format 0, which ISO/IEC 13818-2 Table 6-5 leaves reserved."),
    };

    if (this.ChromaFormat == MpegChromaFormat.Yuv444)
      throw new NotSupportedException(
        "This MPEG-2 sequence states chroma_format 3 (4:4:4), which ISO/IEC 13818-2 6.3.5 permits only in the High "
        + "profile. This decoder reads 4:2:0 and 4:2:2; 4:4:4 is not implemented, and is refused rather than guessed "
        + "at because no encoder available here produces one to check the result against.");

    this.Width |= reader.ReadBits(2) << 12;  // horizontal_size_extension
    this.Height |= reader.ReadBits(2) << 12; // vertical_size_extension

    reader.ReadBits(12); // bit_rate_extension
    if (reader.ReadBit() != 1)
      throw new InvalidDataException(
        "The marker bit in the MPEG-2 sequence extension is zero, so the stream is corrupt or this is not a sequence "
        + "extension.");

    reader.ReadBits(8); // vbv_buffer_size_extension
    reader.ReadBit();   // low_delay
    reader.ReadBits(2); // frame_rate_extension_n
    reader.ReadBits(5); // frame_rate_extension_d

    this.IsMpeg2 = true;
  }

  /// <summary>
  /// Loads whichever of the four quantiser matrices a quant matrix extension carries
  /// (ISO/IEC 13818-2, 6.2.3.2), positioned just past its four-bit identifier.
  /// </summary>
  /// <remarks>
  /// A matrix the extension does not load is left as it was rather than reset to the default, which
  /// is what 13818-2 6.3.11 says: the extension states which matrices it is replacing and says
  /// nothing about the others. And loading the luminance matrix alone replaces the chrominance one
  /// with it, because a stream that gives one matrix is giving the matrix for everything.
  /// </remarks>
  internal void ApplyQuantMatrixExtension(ref MpegBitReader reader) {
    if (reader.ReadBit() == 1) {
      this.IntraMatrix = _ReadMatrix(ref reader);
      this.ChromaIntraMatrix = this.IntraMatrix;
    }

    if (reader.ReadBit() == 1) {
      this.NonIntraMatrix = _ReadMatrix(ref reader);
      this.ChromaNonIntraMatrix = this.NonIntraMatrix;
    }

    if (reader.ReadBit() == 1)
      this.ChromaIntraMatrix = _ReadMatrix(ref reader);

    if (reader.ReadBit() == 1)
      this.ChromaNonIntraMatrix = _ReadMatrix(ref reader);
  }

  /// <summary>
  /// Reads a loaded quantiser matrix and un-zig-zags it.
  /// </summary>
  /// <remarks>
  /// A weight of zero is refused rather than used: it would make every coefficient it applies to
  /// reconstruct as zero regardless of what was coded, which is not a quantiser but a way of
  /// discarding the picture. 11172-2 2.4.2.3 forbids it outright.
  /// <para/>
  /// The zig-zag and not the alternate scan, whatever <c>alternate_scan</c> says: 13818-2 7.3 states
  /// that a downloaded matrix is always in the Figure 7-2 order.
  /// </remarks>
  private static byte[] _ReadMatrix(ref MpegBitReader reader) {
    var matrix = new byte[64];
    for (var scan = 0; scan < 64; ++scan) {
      var weight = reader.ReadBits(8);
      if (weight == 0)
        throw new InvalidDataException(
          $"The quantiser matrix loaded by the MPEG sequence header holds a zero at scan position {scan}, which the standard forbids.");

      matrix[MpegQuantisation.ZigZagScan[scan]] = (byte)weight;
    }

    return matrix;
  }

  /// <summary>Whether another sequence header describes the same picture geometry as this one.</summary>
  internal bool SameGeometryAs(MpegSequenceHeader other) {
    ArgumentNullException.ThrowIfNull(other);

    return this.Width == other.Width && this.Height == other.Height && this.ChromaFormat == other.ChromaFormat;
  }
}
