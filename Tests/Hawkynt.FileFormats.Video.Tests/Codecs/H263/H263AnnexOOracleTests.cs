using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.H263.Tests;

/// <summary>
/// Measures the Annex O temporal B-picture path against FFmpeg's H.263 decoder.
/// </summary>
/// <remarks>
/// Everything else that covers Annex O here decodes a stream this package wrote, with the decoder
/// this package wrote. A grammar both halves read the same wrong way passes every one of those tests
/// while producing a file no player can follow, and the failure does not look like a failure: a
/// B-picture predicted from the wrong anchor still reconstructs a picture, and the error accumulates
/// over a group instead of raising anything. So the claim that this is Annex O and not a private
/// dialect has to be settled by a decoder that was not written here.
/// <para/>
/// The comparison is made on the coded YVU planes rather than on RGB. This package's YUV-to-RGB
/// conversion and FFmpeg's do not agree — measured at up to 63 levels of a channel on identical
/// planes — so an RGB comparison could only be made with a tolerance wide enough to hide exactly the
/// drift this is here to catch.
/// <para/>
/// Two measured disagreements keep that comparison from being bit-exact over a whole clip, and both
/// are bounded at two levels a sample.
/// <para/>
/// The first is normative, and it runs the opposite way from what one would guess. ITU-T H.263 O.4
/// says of direct and bidirectional macroblocks that "the average is calculated by dividing the sum
/// of the two predictions by two (division by truncation)" — the same wording Annex M uses for
/// Improved PB-frames. FFmpeg predicts those macroblocks through its shared MPEG-style averaging,
/// which carries a rounding term, so wherever the two predictions sum to an odd number FFmpeg's
/// sample is one level <i>higher</i> than the Recommendation's. It is FFmpeg departing from the
/// normative text here and not this decoder, so the truncation stays and the disagreement is
/// recorded rather than matched. Its signature is unmistakable and is measured, not assumed: on a
/// 176x144 clip roughly 1900 to 2500 samples of every bidirectional picture are high by one, and
/// between none and eighteen are low.
/// <para/>
/// The second is FFmpeg's inverse discrete cosine transform, which is accuracy-bounded rather than
/// bit-exact. It is visible on the <em>intra</em> pictures of the same clip — where nothing is
/// predicted and nothing is averaged — as a handful of samples one level either way, and it
/// accumulates to two through a chain of predicted pictures.
/// <para/>
/// So the bound is two levels a sample, everywhere, and it is written to those measurements rather
/// than chosen for comfort. What it has to separate is an order of magnitude larger and two-sided:
/// measured on this same clip, a B-picture matched against the wrong anchor, returned in the wrong
/// display order, or predicted from the wrong vector differs from FFmpeg's by 21 to 110 levels over
/// thousands of samples on both sides of zero. The pictures are also required to arrive in display
/// order, each matched picture strictly after the last, so a decoder that returned the right
/// pictures in coding order would fail even if every sample of every one of them were right.
/// <para/>
/// The second test in this fixture is the exact one. A forward-only and a backward-only Annex O
/// macroblock with no texture averages nothing and reconstructs no residual, so neither disagreement
/// can reach it: those macroblocks are required to match FFmpeg on every sample, with no tolerance
/// at all.
/// </remarks>
[TestFixture]
public sealed class H263AnnexOOracleTests {

  private const int _PICTURE_START_CODE = 1 << 5;
  private const int _TIMEOUT_MILLISECONDS = 120_000;

  // ITU-T H.263 Table O.1 MBTYPE indices for the three explicit prediction directions in the form
  // that carries no texture, so a macroblock is its prediction and nothing else.
  private const int _FORWARD_NO_TEXTURE = 2;
  private const int _BACKWARD_NO_TEXTURE = 5;
  private const int _BIDIRECTIONAL_NO_TEXTURE = 8;

  [Test]
  [Category("Oracle")]
  public void FFmpegReadsEveryAnnexOPictureInDisplayOrder() {
    FFmpegOracle.RequireAvailable();

    const int width = 176;
    const int height = 144;
    const int frames = 25; // Three intra groups of twelve, so two group boundaries are crossed.

    var encoder = H263VideoEncoder.Create(_Stream(width, height));
    encoder.BidirectionalPicturesBetweenReferences = 2;

    var packets = new List<CodedPacket>();
    for (var index = 0; index < frames; ++index)
      if (encoder.TryEncode(_Moving(width, height, index), index, out var packet))
        packets.Add(packet);

    packets.AddRange(encoder.Flush());
    Assert.That(packets, Has.Count.EqualTo(frames));

    var kinds = packets.Select(static packet => _ParseHeader(packet.Data.Span).PictureKind).ToArray();
    Assert.Multiple(() => {
      Assert.That(kinds.Count(static kind => kind == H263PictureKind.Intra), Is.GreaterThanOrEqualTo(3),
        "the clip must contain intra pictures");
      Assert.That(kinds.Count(static kind => kind == H263PictureKind.Predicted), Is.GreaterThan(0),
        "the clip must contain forward-predicted anchors");
      Assert.That(kinds.Count(static kind => kind == H263PictureKind.Bidirectional), Is.GreaterThanOrEqualTo(12),
        "the clip must contain bidirectional pictures in every group");
    });

    var expected = _DecodeInDisplayOrder(packets);
    Assert.That(expected, Has.Count.EqualTo(frames));

    var (produced, decoded) = _FFmpegYuv(packets, width, height);

    // FFmpeg's H.263 decoder enters a stream expecting no reordering and learns otherwise from the
    // first B-picture it meets, which makes it emit the anchor of the opening group twice. That is
    // its transition and not a property of the bytes, so one repeated picture is tolerated; what is
    // not is a picture that never arrives, arrives out of order, or differs by a sample.
    Assert.That(produced, Is.InRange(frames, frames + 1),
      $"ffmpeg produced {produced} pictures for {frames} coded ones");

    var frameBytes = _FrameBytes(width, height);
    var at = 0;
    for (var index = 0; index < frames; ++index) {
      var found = -1;
      string? closest = null;
      for (var candidate = at; candidate < produced; ++candidate)
        if (_Matches(decoded, candidate * frameBytes, expected[index], width, height, _Always,
              out var why, tolerance: 2)) {
          found = candidate;
          break;
        } else
          closest ??= $"ffmpeg picture {candidate}: {why}";

      Assert.That(found, Is.Not.EqualTo(-1),
        $"ffmpeg never produced display picture {index} ({expected[index].Kind}); "
        + $"the first unmatched ffmpeg picture is {at} - {closest}");
      at = found + 1;
    }

    // The sequence above is FFmpeg's word on what display order this stream is in. The public
    // decoder has to hand back that same sequence: its reordering is the half of Annex O a round
    // trip cannot check, because a decoder that returns coding order and an encoder that writes it
    // agree with each other perfectly.
    var decoder = H263VideoDecoder.Create(_Stream(width, height));
    var displayed = new List<RawImage>();
    foreach (var packet in packets)
      if (decoder.TryDecode(packet, out var image))
        displayed.Add(image);

    displayed.AddRange(decoder.Flush());
    Assert.That(displayed, Has.Count.EqualTo(frames), "the decoder returned a different number of pictures");

    for (var index = 0; index < frames; ++index)
      Assert.That(displayed[index].PixelData,
        Is.EqualTo(H263ColorConversion.ToRgb24(expected[index].Frame, width, height)),
        $"the decoder returned a different picture than ffmpeg did in display position {index}");
  }

  [Test]
  [Category("Oracle")]
  public void FFmpegAgreesOnForwardBackwardAndBidirectionalBMacroblocks() {
    FFmpegOracle.RequireAvailable();

    const int width = 176;
    const int height = 144;

    // The writer only ever selects the bidirectional macroblock with zero vectors, so forward-only
    // and backward-only prediction and every non-zero Annex O vector would otherwise reach FFmpeg
    // from nowhere. This B-picture is written here rather than by the encoder for that reason.
    var encoder = H263VideoEncoder.Create(_Stream(width, height));
    encoder.BidirectionalPicturesBetweenReferences = 2;

    var anchors = new List<CodedPacket>();
    for (var index = 0; index < 4; ++index)
      if (encoder.TryEncode(_FlatEdged(width, height, index), index, out var packet))
        anchors.Add(packet);

    anchors.AddRange(encoder.Flush());
    Assert.That(anchors, Has.Count.EqualTo(4), "the group must produce one I picture, one P anchor and two B pictures");

    var intra = anchors[0];
    var predicted = anchors[1];
    Assert.Multiple(() => {
      Assert.That(_ParseHeader(intra.Data.Span).PictureKind, Is.EqualTo(H263PictureKind.Intra));
      Assert.That(_ParseHeader(predicted.Data.Span).PictureKind, Is.EqualTo(H263PictureKind.Predicted));
    });

    var bidirectional = new CodedPacket(0, _MixedDirectionBidirectionalPicture(width, height, temporalReference: 1));
    List<CodedPacket> stream = [intra, predicted, bidirectional];

    var expected = _DecodeInDisplayOrder(stream);
    Assert.That(expected, Has.Count.EqualTo(3));

    var (produced, decoded) = _FFmpegYuv(stream, width, height);
    Assert.That(produced, Is.GreaterThanOrEqualTo(3), "ffmpeg did not decode the whole three-picture stream");

    var frameBytes = _FrameBytes(width, height);
    var bPicture = expected.Single(static picture => picture.Kind == H263PictureKind.Bidirectional);

    // A forward-only and a backward-only macroblock average nothing, so FFmpeg's rounding term never
    // enters them and they have to agree sample for sample. Only the bidirectional third may sit one
    // level high. That makes this the sharper of the two oracles: two thirds of the picture is an
    // exact comparison against a decoder nobody here wrote.
    var failure = (string?)null;
    var matchedAt = -1;
    for (var candidate = 0; candidate < produced && matchedAt < 0; ++candidate)
      if (_Matches(decoded, candidate * frameBytes, bPicture, width, height,
            static address => address % 3 == 2, out failure, tolerance: 2))
        matchedAt = candidate;

    Assert.That(matchedAt, Is.GreaterThanOrEqualTo(0),
      "ffmpeg's reconstruction of the B-picture carrying forward, backward and bidirectional macroblocks "
      + $"with non-zero Annex O motion vectors does not match this decoder's: {failure}");
  }

  // ============================================================================================
  // Building a B-picture with all three explicit prediction directions
  // ============================================================================================

  /// <summary>
  /// Writes one Annex O B-picture whose macroblocks cycle through forward-only, backward-only and
  /// bidirectional prediction, each carrying a non-zero motion-vector difference.
  /// </summary>
  /// <remarks>
  /// The differences are non-zero so that the two decoders have to agree about the same-direction
  /// median predictor of O.5 as well as about the prediction itself — a macroblock whose vector is
  /// always zero exercises neither. The border macroblocks are left at difference zero because a
  /// vector there would reach outside the reference, where the two decoders' edge rules are a
  /// separate question from the one this test asks.
  /// </remarks>
  private static byte[] _MixedDirectionBidirectionalPicture(int width, int height, int temporalReference) {
    var writer = new H263BitWriter();
    H263PlusHeaderWriter.Write(writer, width, height, sourceFormat: 2, temporalReference, quantiser: 8,
      H263PictureKind.Bidirectional);

    var macroblockWidth = (width + 15) / 16;
    var macroblockHeight = (height + 15) / 16;

    for (var address = 0; address < macroblockWidth * macroblockHeight; ++address) {
      var column = address % macroblockWidth;
      var row = address / macroblockWidth;
      var onBorder = column == 0 || row == 0
                     || column == macroblockWidth - 1 || row == macroblockHeight - 1;
      var difference = onBorder ? 0 : (address % 5) - 2;

      writer.WriteBit(0); // COD: an explicitly coded macroblock follows.
      switch (address % 3) {
        case 0:
          writer.WriteCode(H263AnnexOVlc.BidirectionalMacroblockTypeCode(_FORWARD_NO_TEXTURE));
          _WriteVectorDifference(writer, difference);
          break;
        case 1:
          writer.WriteCode(H263AnnexOVlc.BidirectionalMacroblockTypeCode(_BACKWARD_NO_TEXTURE));
          _WriteVectorDifference(writer, difference);
          break;
        default:
          writer.WriteCode(H263AnnexOVlc.BidirectionalMacroblockTypeCode(_BIDIRECTIONAL_NO_TEXTURE));
          _WriteVectorDifference(writer, difference);
          _WriteVectorDifference(writer, -difference);
          break;
      }
    }

    return writer.ToArray();
  }

  private static void _WriteVectorDifference(H263BitWriter writer, int difference) {
    writer.WriteCode(H263VlcWriter.MotionVectorDifference(difference));
    writer.WriteCode(H263VlcWriter.MotionVectorDifference(difference));
  }

  // ============================================================================================
  // Decoding both sides
  // ============================================================================================

  private readonly record struct DisplayPicture(H263PictureKind Kind, long Order, H263Frame Frame);

  /// <summary>
  /// Decodes a coding-order packet list into display-ordered reconstructed pictures.
  /// </summary>
  /// <remarks>
  /// The public decoder hands back RGB, and this comparison has to happen in the sample domain the
  /// pictures are actually coded in, so the picture decoders are driven directly here. The reference
  /// bookkeeping is the same one <see cref="H263VideoDecoder"/> performs: a B-picture predicts from
  /// the two anchors around it and never becomes one itself.
  /// </remarks>
  private static List<DisplayPicture> _DecodeInDisplayOrder(IReadOnlyList<CodedPacket> packets) {
    H263Frame? previousReference = null;
    H263Frame? reference = null;
    var pictures = new List<DisplayPicture>();

    foreach (var packet in packets) {
      var reader = new H263BitReader(packet.Data.Span);
      Assert.That(reader.ReadBits(22), Is.EqualTo(_PICTURE_START_CODE));
      var header = H263PictureHeader.Parse(ref reader);
      var target = new H263Frame(header.MacroblockWidth, header.MacroblockHeight);

      if (header.IsBidirectional) {
        var past = previousReference ?? throw new InvalidDataException("no past anchor");
        var future = reference ?? throw new InvalidDataException("no future anchor");
        new H263BidirectionalPictureDecoder(header, target, past, future).DecodePicture(ref reader);
      } else {
        var picture = H263PictureDecoder.BeginPicture(header, target, reference);
        picture.DecodePicture(ref reader);
        target.TemporalReference = header.TemporalReference;
        previousReference = reference;
        reference = target;
      }

      pictures.Add(new(header.PictureKind, packet.PresentationTimestamp ?? pictures.Count, target));
    }

    pictures.Sort(static (left, right) => left.Order.CompareTo(right.Order));
    return pictures;
  }

  private static (int Produced, byte[] Samples) _FFmpegYuv(IReadOnlyList<CodedPacket> packets, int width, int height) {
    var directory = Directory.CreateTempSubdirectory("h263-annexo-oracle");
    try {
      var clip = Path.Combine(directory.FullName, "clip.h263");
      using (var file = File.Create(clip))
        foreach (var packet in packets)
          file.Write(packet.Data.Span);

      var raw = Path.Combine(directory.FullName, "clip.yuv");
      var startInfo = new ProcessStartInfo(FFmpegOracle.ExecutablePath!) {
        RedirectStandardError = true,
        UseShellExecute = false,
      };

      foreach (var argument in new[] {
        "-hide_banner", "-loglevel", "error", "-y", "-f", "h263", "-i", clip,
        "-map", "0:v:0", "-fps_mode", "passthrough",
        "-f", "rawvideo", "-pix_fmt", "yuv420p", raw })
        startInfo.ArgumentList.Add(argument);

      using var process = Process.Start(startInfo)!;
      var diagnostics = process.StandardError.ReadToEnd();
      if (!process.WaitForExit(_TIMEOUT_MILLISECONDS)) {
        try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        Assert.Fail("ffmpeg timed out on the Annex O stream");
      }

      Assert.That(FFmpegOracle.SignificantDiagnostics(diagnostics), Is.Empty,
        "ffmpeg complained about the Annex O stream");
      Assert.That(process.ExitCode, Is.Zero, "ffmpeg refused the Annex O stream");

      var samples = File.ReadAllBytes(raw);
      var frameBytes = _FrameBytes(width, height);
      Assert.That(samples.Length % frameBytes, Is.Zero,
        "ffmpeg produced a partial picture");

      return (samples.Length / frameBytes, samples);
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }

  // ============================================================================================
  // Sample comparison
  // ============================================================================================

  private static int _FrameBytes(int width, int height) => width * height + 2 * ((width + 1) / 2 * ((height + 1) / 2));

  private static readonly Func<int, bool> _Always = static _ => true;

  private static readonly Func<int, bool> _Never = static _ => false;

  /// <summary>
  /// Whether one of FFmpeg's <c>yuv420p</c> pictures is this reconstruction.
  /// </summary>
  /// <remarks>
  /// A macroblock <paramref name="tolerated"/> accepts may differ by <paramref name="tolerance"/>
  /// levels; every other sample must be identical. FFmpeg hands back the displayed picture,
  /// which for a custom format is cropped, while the coded frame here is a whole number of
  /// macroblocks, so the comparison reads the cropped rectangle out of the coded planes rather than
  /// comparing their lengths. H.263 codes Cb before Cr and so does <c>yuv420p</c>.
  /// </remarks>
  private static bool _Matches(
    byte[] samples,
    int offset,
    DisplayPicture picture,
    int width,
    int height,
    Func<int, bool> tolerated,
    out string? failure,
    int tolerance) {
    var frame = picture.Frame;
    var macroblockWidth = (width + 15) / 16;
    var chromaWidth = (width + 1) / 2;
    var chromaHeight = (height + 1) / 2;
    failure = null;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var address = y / 16 * macroblockWidth + x / 16;
        if (_Differs(samples[offset + y * width + x], frame.Luma[y * frame.LumaWidth + x],
              tolerated(address) ? tolerance : 0, "Y", x, y, out failure))
          return false;
      }

    var at = offset + width * height;
    for (var plane = 0; plane < 2; ++plane) {
      var planeSamples = plane == 0 ? frame.Cb : frame.Cr;
      for (var y = 0; y < chromaHeight; ++y)
        for (var x = 0; x < chromaWidth; ++x) {
          var address = y / 8 * macroblockWidth + x / 8;
          if (_Differs(samples[at + y * chromaWidth + x], planeSamples[y * frame.ChromaWidth + x],
                tolerated(address) ? tolerance : 0, plane == 0 ? "Cb" : "Cr", x, y, out failure))
            return false;
        }

      at += chromaWidth * chromaHeight;
    }

    return true;
  }

  private static bool _Differs(
    byte foreign, byte mine, int tolerance, string plane, int x, int y, out string? failure) {
    var delta = foreign - mine;
    if (delta >= -tolerance && delta <= tolerance) {
      failure = null;
      return false;
    }

    failure = $"{plane} at {x},{y}: ffmpeg {foreign}, this decoder {mine}, which is {delta} and the "
              + (tolerance == 0
                ? "macroblock averages nothing and codes nothing, so it must agree exactly"
                : $"most a reconstructed macroblock may differ by is {tolerance}");
    return true;
  }

  // ============================================================================================
  // Source material
  // ============================================================================================

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("H263"),
    Handler = CodecTag.FromCharacters("H263"),
    Width = width,
    Height = height,
    TimeBase = new(1, 25),
    FrameRate = new(25, 1),
  };

  private static RawImage _Moving(int width, int height, int phase) {
    var pixels = new byte[width * height * 3];
    var left = 16 + phase * 5 % Math.Max(1, width - 64);
    var top = 16 + phase * 3 % Math.Max(1, height - 64);

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        var inBox = x >= left && x < left + 32 && y >= top && y < top + 32;
        pixels[at] = (byte)(inBox ? 225 : 48 + ((x / 16 + y / 16) & 1) * 32);
        pixels[at + 1] = (byte)(inBox ? 64 : 112);
        pixels[at + 2] = (byte)(inBox ? 168 : 176);
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  /// <summary>A moving picture with a constant border, so a vector that reaches the edge reads the same either way.</summary>
  private static RawImage _FlatEdged(int width, int height, int phase) {
    var frame = _Moving(width, height, phase);
    var pixels = frame.PixelData;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        if (x >= 32 && x < width - 32 && y >= 32 && y < height - 32)
          continue;

        var at = (y * width + x) * 3;
        pixels[at] = 90;
        pixels[at + 1] = 110;
        pixels[at + 2] = 140;
      }

    return frame;
  }

  private static H263PictureHeader _ParseHeader(ReadOnlySpan<byte> data) {
    var reader = new H263BitReader(data);
    Assert.That(reader.ReadBits(22), Is.EqualTo(_PICTURE_START_CODE));
    return H263PictureHeader.Parse(ref reader);
  }
}
