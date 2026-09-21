using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.H265.Tests;

/// <summary>
/// Requires FFmpeg to reconstruct exactly the samples this encoder believes it wrote.
/// </summary>
/// <remarks>
/// Decoding HEVC is normative: for a conforming stream every decoder produces bit-identical samples.
/// So the encoder's own reconstruction is not merely a quality target, it is a prediction about what
/// any decoder will do, and FFmpeg either confirms it sample for sample or the stream is wrong.
/// <para/>
/// This is the assertion a round trip through this package's own decoder cannot make. Encoder and
/// decoder share one copy of the motion derivation — deliberately, see
/// <see cref="IH265MotionContext"/> — so a mistake in it is made identically on both sides and the
/// round trip agrees perfectly on the wrong picture. That already happened twice here: a
/// context-initialisation row transcribed from the wrong column, and clause 6.4.2's rule for a
/// neighbour inside the current coding block. Neither left any trace in the stream; both were found
/// by an outside decoder disagreeing.
/// <para/>
/// Each test states one thing the encoder can emit and requires FFmpeg to land on the same samples.
/// The comparison is exact rather than tolerant: a tolerance measures quality, and the question here
/// is agreement.
/// </remarks>
[TestFixture]
public sealed class H265FfmpegReconstructionTests {

  private const int _SIZE = 64;
  private const int _QUANTISER = 20;
  private const int _CHROMA = _SIZE / 2;

  [Test]
  [Category("Oracle")]
  public void FFmpegReconstructsHorizontalPredictionHalvesExactly() {
    var reference = _Texture();
    var source = _Displaced(reference, static (x, y) => (y & 31) < 16 ? 2 : 9);

    var coded = _CodePredicted(source, [reference], poc: 2, pastPocs: [0], minimumCuLog2: 5);
    Assert.That(coded.Encoder.CodingBlockPartitionMode, Is.EqualTo(H265PartitionMode.HorizontalHalves),
      "the content was built so that a horizontal half partition wins; without one this proves nothing");

    _RequireFFmpegAgrees(reference, [coded]);
  }

  /// <summary>
  /// The vertical counterpart, which is not the same test.
  /// </summary>
  /// <remarks>
  /// Prediction blocks are decoded in partition order and the minimum blocks they cover are
  /// addressed in z-scan order, and the two orders agree for a horizontal split and disagree for a
  /// vertical one. So a derivation that asks the z-scan a question clause 6.4.2 does not ask it is
  /// right for <c>PART_2NxN</c> and wrong for <c>PART_Nx2N</c>, and only this case says so.
  /// </remarks>
  [Test]
  [Category("Oracle")]
  public void FFmpegReconstructsVerticalPredictionHalvesExactly() {
    var reference = _Texture();
    var source = _Displaced(reference, static (x, _) => (x & 31) < 16 ? 2 : 9);

    var coded = _CodePredicted(source, [reference], poc: 2, pastPocs: [0], minimumCuLog2: 5);
    Assert.That(coded.Encoder.CodingBlockPartitionMode, Is.EqualTo(H265PartitionMode.VerticalHalves),
      "the content was built so that a vertical half partition wins; without one this proves nothing");

    _RequireFFmpegAgrees(reference, [coded]);
  }

  [Test]
  [Category("Oracle")]
  public void FFmpegReconstructsHalvesOfASplitCodingUnitExactly() {
    var reference = _Texture();
    var source = _Displaced(reference, static (x, y) => ((x & 15) < 8 ? 2 : 9) + ((y & 15) < 8 ? 0 : 12));

    var coded = _CodePredicted(source, [reference], poc: 2, pastPocs: [0], minimumCuLog2: 4, adaptive: true);
    _RequireFFmpegAgrees(reference, [coded]);
  }

  /// <summary>
  /// A predicted picture that has to name which of two references each block came from.
  /// </summary>
  /// <remarks>
  /// The older reference carries the content and the newer one does not, so the search can only
  /// reach it through <c>ref_idx_l0</c>. A stream whose reference indices were written wrongly still
  /// decodes; it just predicts from the other picture.
  /// </remarks>
  [Test]
  [Category("Oracle")]
  public void FFmpegReconstructsASecondReferenceIndexExactly() {
    var older = _Texture();
    var newer = _Flat(17);
    var source = _Displaced(older, static (_, _) => 4);

    var middle = _CodePredicted(newer, [older], poc: 2, pastPocs: [0], minimumCuLog2: 4, adaptive: true);
    var coded = _CodePredicted(
      source, [middle.Encoder.Reconstruction, older], poc: 4, pastPocs: [2, 0], minimumCuLog2: 4, adaptive: true);

    var motion = coded.Encoder.MotionAt(coded.Encoder.BlockIndexAt(_SIZE / 2, _SIZE / 2));
    Assert.That(motion.RefIdxL0, Is.EqualTo(1),
      "the content only exists in the second reference; a search that stayed on index zero tests nothing");

    _RequireFFmpegAgrees(older, [middle, coded]);
  }

  /// <summary>
  /// A bidirectional picture, which is where a shared table was wrong once before.
  /// </summary>
  /// <remarks>
  /// Its two lists name the same two pictures in opposite orders, so every block states a reference
  /// index in whichever list it took. The content is built so that the left half of every coding unit
  /// is what the past reference holds and the right half is what the future one holds, which makes a
  /// vertical split win and sends its two prediction blocks to different lists — the case where
  /// reference indices, partition shape and the direction each block predicts from all have to be
  /// right at once.
  /// </remarks>
  [Test]
  [Category("Oracle")]
  public void FFmpegReconstructsABidirectionalPictureExactly() {
    var past = _Texture();
    var futureSource = _Displaced(past, static (_, _) => 11);
    var future = _CodePredicted(futureSource, [past], poc: 4, pastPocs: [0], minimumCuLog2: 4, adaptive: true);

    var source = _Displaced(past, static (x, _) => (x & 31) < 16 ? 0 : 11);
    var bidirectional = _CodeBidirectional(
      source, poc: 2, pastPocs: [0], futurePoc: 4,
      list0: [past, future.Encoder.Reconstruction],
      list1: [future.Encoder.Reconstruction, past]);

    var left = bidirectional.Encoder.MotionAt(bidirectional.Encoder.BlockIndexAt(4, 8));
    var right = bidirectional.Encoder.MotionAt(bidirectional.Encoder.BlockIndexAt(20, 8));
    Assert.Multiple(() => {
      Assert.That(bidirectional.Encoder.CodingBlockPartitionMode, Is.EqualTo(H265PartitionMode.VerticalHalves),
        "the content was built so that a vertical half partition wins");
      Assert.That(left.PredictL0, Is.True, "the left half is what the past reference holds");
      Assert.That(right.PredictL1, Is.True, "the right half is what the future reference holds");
    });

    _RequireFFmpegAgrees(past, [future, bidirectional], displayOrder: [2, 1]);
  }

  /// <summary>
  /// Coding units whose samples were sent uncompressed, read by the general frame decoder.
  /// </summary>
  /// <remarks>
  /// Reading one means leaving the arithmetic decoder at <c>pcm_flag</c>, taking the samples as raw
  /// bits at the sequence's own depth, and restarting the decoder afterwards while keeping the
  /// probability contexts it had adapted. Keeping them is the part a round trip cannot check: a
  /// decoder that reinitialised them would disagree with every other decoder and with nothing in
  /// this package. The <c>split_cu_flag</c> in front of each later coding unit is context coded, so
  /// a stream of several such units only decodes if the contexts did survive.
  /// </remarks>
  [Test]
  [Category("Oracle")]
  public void FFmpegAndTheGeneralFrameDecoderAgreeOnPulseCodeModulatedCodingUnits() {
    FFmpegOracle.RequireAvailable();

    var luma = new byte[_SIZE * _SIZE];
    var cb = new byte[_CHROMA * _CHROMA];
    var cr = new byte[_CHROMA * _CHROMA];
    for (var i = 0; i < luma.Length; ++i)
      luma[i] = (byte)((i * 37 + (i >> 5) * 11) & 0xFF);
    for (var i = 0; i < cb.Length; ++i) {
      cb[i] = (byte)(64 + (i * 29 & 127));
      cr[i] = (byte)(64 + (i * 43 & 127));
    }

    var sets = _ParameterSets();
    var rbsp = H265PcmStillCodec._BuildSlice(luma, cb, cr, _SIZE, _SIZE);
    var nal = new H265NalUnit(H265NalUnitType.IdrWithNoLeadingPictures, 0, 0, rbsp, []);
    var header = H265SliceHeader.Parse(nal, sets.SpsById, sets.PpsById);

    var decoder = new H265FrameDecoder(sets.Sps, sets.Pps);
    decoder.DecodeSliceSegment(header, [[], []]);
    decoder.RefuseIfIncomplete();

    var decoded = _RunFFmpeg(_AnnexB(sets, luma, cb, cr, []), 1);
    Assert.Multiple(() => {
      _AssertPlane(decoder.Picture.Luma, _SIZE, luma, _SIZE, _SIZE, "this decoder's luminance");
      _AssertFfmpegPlane(decoded, 0, 0, luma, _SIZE, _SIZE, "ffmpeg's luminance");
      _AssertFfmpegPlane(decoded, 0, _SIZE * _SIZE, cb, _CHROMA, _CHROMA, "ffmpeg's blue chrominance");
    });
  }

  // ── driving the encoder ─────────────────────────────────────────────────────

  private sealed record Coded(byte[] Nal, H265InterPictureEncoder Encoder);

  private static Coded _CodePredicted(
    H265Picture source, IReadOnlyList<H265Picture> list0, int poc, IReadOnlyList<int> pastPocs,
    int minimumCuLog2, bool adaptive = false) {
    var sets = _ParameterSets();
    var headerBits = _SliceHeader(
      H265SliceType.P, poc, pastPocs, futurePoc: null, activeL0: list0.Count, activeL1: 0);
    var header = _ParseHeader(sets, headerBits, H265NalUnitType.TrailingReference);
    var encoder = new H265InterPictureEncoder(
      sets.Sps, sets.Pps, header, _WithOrderCount(source, poc), list0, [], header.SliceQpY,
      adaptive, minimumCuLog2);

    return new(_SliceNal(headerBits, encoder.Encode(), H265NalUnitType.TrailingReference), encoder);
  }

  private static Coded _CodeBidirectional(
    H265Picture source, int poc, IReadOnlyList<int> pastPocs, int futurePoc,
    IReadOnlyList<H265Picture> list0, IReadOnlyList<H265Picture> list1) {
    var sets = _ParameterSets();
    var headerBits = _SliceHeader(
      H265SliceType.B, poc, pastPocs, futurePoc, list0.Count, list1.Count);
    var header = _ParseHeader(sets, headerBits, H265NalUnitType.TrailingNonReference);
    var encoder = new H265InterPictureEncoder(
      sets.Sps, sets.Pps, header, _WithOrderCount(source, poc), list0, list1, header.SliceQpY,
      adaptiveCodingUnits: true, minimumCuLog2: 4);

    return new(_SliceNal(headerBits, encoder.Encode(), H265NalUnitType.TrailingNonReference), encoder);
  }

  /// <summary>
  /// Hands FFmpeg the whole stream and requires every coded picture back exactly as reconstructed.
  /// </summary>
  /// <param name="displayOrder">
  /// Which decoded picture each coded one is, where coding order is not display order. Omitted where
  /// the two agree.
  /// </param>
  private static void _RequireFFmpegAgrees(
    H265Picture key, IReadOnlyList<Coded> coded, IReadOnlyList<int>? displayOrder = null) {
    FFmpegOracle.RequireAvailable();

    var sets = _ParameterSets();
    var luma = _Narrow(key.Luma, _SIZE * _SIZE);
    var cb = _Narrow(key.Cb, _CHROMA * _CHROMA);
    var cr = _Narrow(key.Cr, _CHROMA * _CHROMA);

    var nals = new List<byte[]>();
    foreach (var picture in coded)
      nals.Add(picture.Nal);

    var decoded = _RunFFmpeg(_AnnexB(sets, luma, cb, cr, nals), coded.Count + 1);

    Assert.Multiple(() => {
      _AssertFfmpegPlane(decoded, 0, 0, luma, _SIZE, _SIZE, "the key picture's luminance");

      for (var index = 0; index < coded.Count; ++index) {
        var frame = displayOrder == null ? index + 1 : displayOrder[index];
        var reconstruction = coded[index].Encoder.Reconstruction;
        _AssertFfmpegWidePlane(
          decoded, frame, 0, reconstruction.Luma, _SIZE, _SIZE, _SIZE,
          $"picture {index}'s luminance");
        _AssertFfmpegWidePlane(
          decoded, frame, _SIZE * _SIZE, reconstruction.Cb, reconstruction.ChromaWidth, _CHROMA, _CHROMA,
          $"picture {index}'s blue chrominance");
        _AssertFfmpegWidePlane(
          decoded, frame, _SIZE * _SIZE + _CHROMA * _CHROMA, reconstruction.Cr, reconstruction.ChromaWidth,
          _CHROMA, _CHROMA, $"picture {index}'s red chrominance");
      }
    });
  }

  // ── the stream ──────────────────────────────────────────────────────────────

  private sealed record Sets(
    H265SequenceParameterSet Sps,
    H265PictureParameterSet Pps,
    byte[] SpsBytes,
    byte[] PpsBytes,
    byte Level) {

    public Dictionary<int, H265SequenceParameterSet> SpsById => new() { [0] = this.Sps };
    public Dictionary<int, H265PictureParameterSet> PpsById => new() { [0] = this.Pps };
  }

  private static Sets _ParameterSets() {
    var level = H265PcmStillCodec._SmallestLevelFor(_SIZE, _SIZE);
    var sps = H265PcmStillCodec._BuildSps(
      _SIZE, _SIZE, _SIZE, _SIZE, level,
      maxDecPicBufferingMinus1: 3, log2MaxPocLsbMinus4: 4, maxNumReorderPics: 2,
      maxTransformHierarchyDepthInter: 1);
    var pps = H265PcmStillCodec._BuildPps();

    return new(
      H265SequenceParameterSet.Parse(sps), H265PictureParameterSet.Parse(pps), sps, pps, level);
  }

  private static byte[] _AnnexB(
    Sets sets, byte[] luma, byte[] cb, byte[] cr, IReadOnlyList<byte[]> inter) {
    var units = new List<byte[]> {
      H265PcmStillCodec._MakeNal(H265NalUnitType.VideoParameterSet, H265PcmStillCodec._BuildVps(sets.Level)),
      H265PcmStillCodec._MakeNal(H265NalUnitType.SequenceParameterSet, sets.SpsBytes),
      H265PcmStillCodec._MakeNal(H265NalUnitType.PictureParameterSet, sets.PpsBytes),
      H265PcmStillCodec._MakeNal(
        H265NalUnitType.IdrWithNoLeadingPictures, H265PcmStillCodec._BuildSlice(luma, cb, cr, _SIZE, _SIZE)),
    };

    foreach (var nal in inter)
      units.Add(nal);

    var result = new List<byte>();
    foreach (var unit in units) {
      result.AddRange([0, 0, 0, 1]);
      result.AddRange(unit);
    }

    return [.. result];
  }

  private static byte[] _SliceNal(byte[] headerBits, byte[] payload, H265NalUnitType type) {
    var rbsp = new byte[headerBits.Length + payload.Length];
    headerBits.CopyTo(rbsp, 0);
    payload.CopyTo(rbsp, headerBits.Length);

    return H265PcmStillCodec._MakeNal(type, rbsp);
  }

  private static H265SliceHeader _ParseHeader(Sets sets, byte[] headerBits, H265NalUnitType type) {
    var rbsp = new byte[headerBits.Length + 1];
    headerBits.CopyTo(rbsp, 0);
    rbsp[^1] = 0x80;

    return H265SliceHeader.Parse(new H265NalUnit(type, 0, 0, rbsp, []), sets.SpsById, sets.PpsById);
  }

  private static byte[] _SliceHeader(
    H265SliceType type, int poc, IReadOnlyList<int> pastPocs, int? futurePoc, int activeL0, int activeL1) {
    var w = new H265PcmStillCodec.Bits();
    w.WriteBit(1);  // first_slice_segment_in_pic_flag
    w.WriteUe(0);   // slice_pic_parameter_set_id
    w.WriteUe(type == H265SliceType.B ? 0u : 1u);
    w.WriteBits((uint)poc, 8); // slice_pic_order_cnt_lsb
    w.WriteBit(0);  // short_term_ref_pic_set_sps_flag

    w.WriteUe((uint)pastPocs.Count);            // num_negative_pics
    w.WriteUe(futurePoc.HasValue ? 1u : 0u);    // num_positive_pics

    var previous = poc;
    foreach (var pastPoc in pastPocs) {
      w.WriteUe((uint)(previous - pastPoc - 1)); // delta_poc_s0_minus1
      w.WriteBit(1);                             // used_by_curr_pic_s0_flag
      previous = pastPoc;
    }

    if (futurePoc.HasValue) {
      w.WriteUe((uint)(futurePoc.Value - poc - 1)); // delta_poc_s1_minus1
      w.WriteBit(type == H265SliceType.B ? 1 : 0);  // used_by_curr_pic_s1_flag
    }

    var overrideActive = activeL0 != 1 || (type == H265SliceType.B && activeL1 != 1);
    w.WriteBit(overrideActive ? 1 : 0);
    if (overrideActive) {
      w.WriteUe((uint)(activeL0 - 1));
      if (type == H265SliceType.B)
        w.WriteUe((uint)(activeL1 - 1));
    }

    if (type == H265SliceType.B)
      w.WriteBit(0); // mvd_l1_zero_flag

    w.WriteUe(4);                 // five_minus_max_num_merge_cand
    w.WriteSe(_QUANTISER - 26);   // slice_qp_delta
    w.WriteByteAlignment();
    return w.ToArray();
  }

  private static byte[] _RunFFmpeg(byte[] annexB, int expectedFrames) {
    var directory = Directory.CreateTempSubdirectory("h265-reconstruction");
    try {
      var path = Path.Combine(directory.FullName, "clip.265");
      File.WriteAllBytes(path, annexB);
      var raw = Path.Combine(directory.FullName, "decoded.yuv");

      var startInfo = new ProcessStartInfo(FFmpegOracle.ExecutablePath!) {
        RedirectStandardError = true,
        UseShellExecute = false,
      };
      foreach (var argument in new[] {
        "-hide_banner", "-loglevel", "error", "-y", "-i", path,
        "-f", "rawvideo", "-pix_fmt", "yuv420p", raw })
        startInfo.ArgumentList.Add(argument);

      using var process = Process.Start(startInfo)!;
      var diagnostics = process.StandardError.ReadToEnd().Trim();
      process.WaitForExit(60_000);

      // At this log level FFmpeg says nothing about a stream it read cleanly, so anything at all on
      // the error channel is it telling us it patched over something.
      Assert.That(process.ExitCode, Is.Zero, $"ffmpeg refused the stream: {diagnostics}");
      Assert.That(diagnostics, Is.Empty, "ffmpeg complained about the stream");

      var decoded = File.ReadAllBytes(raw);
      var frameBytes = _SIZE * _SIZE + 2 * _CHROMA * _CHROMA;
      Assert.That(decoded.Length, Is.EqualTo(frameBytes * expectedFrames),
        $"ffmpeg produced {decoded.Length / (double)frameBytes} pictures rather than {expectedFrames}");

      return decoded;
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }

  // ── comparison ──────────────────────────────────────────────────────────────

  private static void _AssertFfmpegPlane(
    byte[] decoded, int frame, int planeOffset, byte[] expected, int width, int height, string what) {
    var frameBytes = _SIZE * _SIZE + 2 * _CHROMA * _CHROMA;
    var at = frame * frameBytes + planeOffset;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        if (decoded[at + y * width + x] != expected[y * width + x]) {
          Assert.Fail(
            $"{what} differs from ffmpeg's at ({x}, {y}): {expected[y * width + x]} against "
            + $"{decoded[at + y * width + x]}");
          return;
        }
  }

  private static void _AssertFfmpegWidePlane(
    byte[] decoded, int frame, int planeOffset, ushort[] expected, int stride,
    int width, int height, string what) {
    var frameBytes = _SIZE * _SIZE + 2 * _CHROMA * _CHROMA;
    var at = frame * frameBytes + planeOffset;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        if (decoded[at + y * width + x] != expected[y * stride + x]) {
          Assert.Fail(
            $"{what} is not what ffmpeg reconstructed at ({x}, {y}): this encoder holds "
            + $"{expected[y * stride + x]}, ffmpeg decoded {decoded[at + y * width + x]}");
          return;
        }
  }

  private static void _AssertPlane(
    ushort[] actual, int stride, byte[] expected, int width, int height, string what) {
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        if (actual[y * stride + x] != expected[y * width + x]) {
          Assert.Fail($"{what} differs at ({x}, {y}): {actual[y * stride + x]} against {expected[y * width + x]}");
          return;
        }
  }

  // ── pictures ────────────────────────────────────────────────────────────────

  private static H265Picture _Texture() {
    var picture = new H265Picture(_SIZE, _SIZE, 2) { PictureOrderCount = 0 };
    for (var i = 0; i < picture.Luma.Length; ++i)
      picture.Luma[i] = (ushort)((i * 37 + (i >> 6) * 91 + ((i * 13) ^ (i >> 3)) * 7) & 0xFF);

    Array.Fill(picture.Cb, (ushort)128);
    Array.Fill(picture.Cr, (ushort)128);
    return picture;
  }

  private static H265Picture _Flat(ushort value) {
    var picture = new H265Picture(_SIZE, _SIZE, 2) { PictureOrderCount = 0 };
    Array.Fill(picture.Luma, value);
    Array.Fill(picture.Cb, (ushort)128);
    Array.Fill(picture.Cr, (ushort)128);
    return picture;
  }

  private static H265Picture _Displaced(H265Picture reference, Func<int, int, int> shift) {
    var picture = new H265Picture(_SIZE, _SIZE, 2);
    for (var y = 0; y < _SIZE; ++y)
      for (var x = 0; x < _SIZE; ++x)
        picture.Luma[y * _SIZE + x] =
          reference.Luma[y * _SIZE + Math.Clamp(x + shift(x, y), 0, _SIZE - 1)];

    Array.Fill(picture.Cb, (ushort)128);
    Array.Fill(picture.Cr, (ushort)128);
    return picture;
  }

  private static H265Picture _WithOrderCount(H265Picture picture, int poc) {
    picture.PictureOrderCount = poc;
    return picture;
  }

  private static byte[] _Narrow(ushort[] plane, int count) {
    var result = new byte[count];
    for (var i = 0; i < count; ++i)
      result[i] = (byte)plane[i];

    return result;
  }
}
