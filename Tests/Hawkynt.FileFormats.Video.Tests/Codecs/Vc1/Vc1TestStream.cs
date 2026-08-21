using System;
using System.Collections.Generic;

namespace FileFormat.Codecs.Vc1.Tests;

/// <summary>Writes bits into a stream most significant bit first, the way VC-1 is read.</summary>
internal sealed class Vc1BitWriter {

  private readonly List<byte> _bytes = [];
  private int _partial;
  private int _used;

  internal void Bit(int value) {
    this._partial = (this._partial << 1) | (value & 1);
    if (++this._used != 8)
      return;

    this._bytes.Add((byte)this._partial);
    this._partial = 0;
    this._used = 0;
  }

  internal void Bits(int value, int count) {
    for (var i = count - 1; i >= 0; --i)
      this.Bit((value >> i) & 1);
  }

  /// <summary>Writes one entry of a code table, as the standard prints it: a codeword and its width.</summary>
  internal void Code(int codeword, int length) => this.Bits(codeword, length);

  internal byte[] ToArray() {
    var result = new List<byte>(this._bytes);
    if (this._used > 0)
      result.Add((byte)(this._partial << (8 - this._used)));

    return result.ToArray();
  }
}

/// <summary>
/// Builds Simple and Main profile VC-1 streams by hand, so the decoder can be tested without a sample
/// in the tree.
/// </summary>
/// <remarks>
/// There is no VC-1 encoder here and ffmpeg has none either — it decodes the format and does not write
/// it — so a test stream cannot be produced by encoding one. What can be built by hand is a picture
/// whose every syntax element is chosen for a reconstruction that can be worked out on paper: a
/// picture where no block carries an AC coefficient and every DC differential is nought reconstructs
/// to one value repeated over the whole frame, and that value follows from the quantiser and the
/// two constants the standard attaches to it.
/// <para/>
/// That is a narrow picture but it exercises a wide path: the sequence header, the picture header, the
/// predicted coded block pattern, the DC differential tables, DC prediction against absent neighbours,
/// inverse quantisation, the inverse transform, overlap smoothing and the rule about when the constant
/// 128 is added. What it cannot reach — the AC coding sets, the escape modes, the scans — is what the
/// measurement against ffmpeg covers, and the table tests beside this cover the tables themselves.
/// </remarks>
internal static class Vc1TestStream {

  /// <summary>Assembles a <c>STRUCT_C</c>, the thirty-two bits a container carries as private data.</summary>
  /// <param name="profile">0 for Simple, 4 for Main, 12 for Advanced.</param>
  /// <param name="quantiser">0 implicit, 1 stated per picture, 2 nonuniform throughout, 3 uniform throughout.</param>
  internal static byte[] SequenceHeader(
    int profile = 0,
    bool loopFilter = false,
    bool multiResolution = false,
    bool fastUvMotionCompensation = false,
    bool extendedMotionVectors = false,
    int differentialQuantisation = 0,
    bool variableSizedTransform = false,
    bool overlap = false,
    bool syncMarker = false,
    bool rangeReduction = false,
    int maxBFrames = 0,
    int quantiser = 3,
    bool frameInterpolation = false,
    int reserved3 = 0,
    int reserved4 = 1,
    int reserved5 = 0,
    int reserved6 = 1) {
    var writer = new Vc1BitWriter();
    writer.Bits(profile, 4);
    writer.Bits(0, 3);
    writer.Bits(0, 5);
    writer.Bit(loopFilter ? 1 : 0);
    writer.Bit(reserved3);
    writer.Bit(multiResolution ? 1 : 0);
    writer.Bit(reserved4);
    writer.Bit(fastUvMotionCompensation ? 1 : 0);
    writer.Bit(extendedMotionVectors ? 1 : 0);
    writer.Bits(differentialQuantisation, 2);
    writer.Bit(variableSizedTransform ? 1 : 0);
    writer.Bit(reserved5);
    writer.Bit(overlap ? 1 : 0);
    writer.Bit(syncMarker ? 1 : 0);
    writer.Bit(rangeReduction ? 1 : 0);
    writer.Bits(maxBFrames, 3);
    writer.Bits(quantiser, 2);
    writer.Bit(frameInterpolation ? 1 : 0);
    writer.Bit(reserved6);
    return writer.ToArray();
  }

  /// <summary>
  /// Wraps a sequence header in the <c>BITMAPINFOHEADER</c> a container hands it over inside.
  /// </summary>
  /// <param name="declaredHeaderSize">
  /// What to write in <c>biSize</c>. The default is what ASF and AVI actually write for a Windows
  /// Media stream — the structure and the codec's own data together, so 44 for a forty-byte header
  /// and four bytes of sequence header. A reader that stepped over that many bytes to find the
  /// sequence header would step over the sequence header itself and land back on this field.
  /// </param>
  internal static byte[] AsCodecPrivateData(byte[] sequenceHeader, int width, int height, int? declaredHeaderSize = null) {
    ArgumentNullException.ThrowIfNull(sequenceHeader);

    var data = new byte[40 + sequenceHeader.Length];
    _WriteInt32(data, 0, declaredHeaderSize ?? 40 + sequenceHeader.Length);
    _WriteInt32(data, 4, width);
    _WriteInt32(data, 8, height);
    data[14] = 24;
    data[16] = (byte)'W';
    data[17] = (byte)'M';
    data[18] = (byte)'V';
    data[19] = (byte)'3';
    sequenceHeader.CopyTo(data, 40);
    return data;
  }

  private static void _WriteInt32(byte[] data, int at, int value) {
    data[at] = (byte)value;
    data[at + 1] = (byte)(value >> 8);
    data[at + 2] = (byte)(value >> 16);
    data[at + 3] = (byte)(value >> 24);
  }

  /// <summary>
  /// Builds an intra picture in which no block carries an AC coefficient and every DC differential is
  /// nought, over a frame of the given size in macroblocks.
  /// </summary>
  /// <remarks>
  /// Every element is the shortest codeword of its table. The coded block pattern is index nought of
  /// Table 168, one bit; with every neighbour uncoded the prediction leaves all six bits clear, so no
  /// block reads an AC coefficient. The DC differential is index nought of the low-motion tables — one
  /// bit for luma and two for colour-difference — which is the value nought, and a differential of
  /// nought carries no sign bit after it.
  /// </remarks>
  /// <param name="quantiserIndex">PQINDEX, which with an explicit quantiser is the step itself.</param>
  /// <param name="halfStep">Written only where the standard states the field, which is PQINDEX of 8 or less.</param>
  internal static byte[] FlatIntraPicture(int macroblockWidth, int macroblockHeight, int quantiserIndex, bool halfStep = false) {
    var writer = new Vc1BitWriter();

    // FRMCNT, which every Simple and Main profile picture header carries and nothing decodes.
    writer.Bits(0, 2);

    // PTYPE: one bit, because the sequence header states no B frames.
    writer.Bit(0);

    // BF, the encoder's buffer fullness, which nothing decodes either.
    writer.Bits(0, 7);

    writer.Bits(quantiserIndex, 5);
    if (quantiserIndex <= 8)
      writer.Bit(halfStep ? 1 : 0);

    // TRANSACFRM and TRANSACFRM2, both index nought; then TRANSDCTAB, the low-motion tables.
    writer.Bit(0);
    writer.Bit(0);
    writer.Bit(0);

    for (var mb = 0; mb < macroblockWidth * macroblockHeight; ++mb) {
      // Table 168 index 0: codeword 1 in one bit.
      writer.Code(1, 1);

      // ACPRED, off.
      writer.Bit(0);

      // Four luma blocks: Table 173 index 0, codeword 1 in one bit. Then two colour-difference blocks:
      // Table 174 index 0, codeword 0 in two bits.
      for (var i = 0; i < 4; ++i)
        writer.Code(1, 1);

      for (var i = 0; i < 2; ++i)
        writer.Code(0, 2);
    }

    return writer.ToArray();
  }

  /// <summary>Builds a picture header whose PTYPE says the picture is predicted rather than intra.</summary>
  internal static byte[] PredictedPicture() {
    var writer = new Vc1BitWriter();
    writer.Bits(0, 2);
    writer.Bit(1);
    writer.Bits(0, 16);
    return writer.ToArray();
  }
}
