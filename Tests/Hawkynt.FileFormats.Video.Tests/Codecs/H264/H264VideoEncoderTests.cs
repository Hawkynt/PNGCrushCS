using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.H264Video;
using FileFormat.Matroska;
using FileFormat.Mp4;
using Hawkynt.FileFormats.Video.Tests;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.H264.Tests;

[TestFixture]
public sealed class H264VideoEncoderTests {

  [Test]
  [Category("Oracle")]
  public void FFmpegReadsReorderedIPBClipBackAsTheFramesThatWentIn() {
    FFmpegOracle.RequireAvailable();

    const int width = 64;
    const int height = 48;
    const int frames = 6;

    var encoder = H264VideoEncoder.Create(_Stream(width, height));
    var packets = new List<CodedPacket>();
    var sources = new List<RawImage>();

    for (var index = 0; index < frames; ++index) {
      var picture = _MovingSquare(width, height, index);
      sources.Add(picture);
      if (encoder.TryEncode(picture, index, out var packet))
        packets.Add(packet);
    }
    packets.AddRange(encoder.Flush());

    Assert.Multiple(() => {
      Assert.That(packets, Has.Count.EqualTo(frames));
      Assert.That(packets.Select(packet => packet.PresentationTimestamp),
        Is.EqualTo(new long?[] { 0, 2, 1, 4, 3, 5 }),
        "B pictures must be emitted after their future reference rather than in display order");
      Assert.That(packets.Select(packet => packet.DecodeTimestamp),
        Is.EqualTo(new long?[] { 0, 1, 2, 3, 4, 5 }));
    });

    var directory = Directory.CreateTempSubdirectory("h264-oracle");
    try {
      var path = Path.Combine(directory.FullName, "clip.mp4");
      File.WriteAllBytes(path, VideoIO.Mux<Mp4Writer>([encoder.DescribeStream()], packets));

      var raw = Path.Combine(directory.FullName, "decoded.rgb");
      var startInfo = new ProcessStartInfo(FFmpegOracle.ExecutablePath!) {
        RedirectStandardError = true,
        UseShellExecute = false,
      };
      foreach (var argument in new[] {
        "-hide_banner", "-loglevel", "error", "-i", path, "-f", "rawvideo", "-pix_fmt", "rgb24", raw })
        startInfo.ArgumentList.Add(argument);

      using var process = Process.Start(startInfo)!;
      var diagnostics = process.StandardError.ReadToEnd();
      process.WaitForExit(60_000);

      Assert.That(process.ExitCode, Is.Zero, $"ffmpeg refused the stream: {diagnostics}");

      var decoded = File.ReadAllBytes(raw);
      var frameBytes = width * height * 3;
      Assert.That(decoded.Length / frameBytes, Is.EqualTo(frames),
        "ffmpeg read a different number of pictures than were written");

      for (var index = 0; index < frames; ++index) {
        var total = 0L;
        for (var offset = 0; offset < frameBytes; ++offset)
          total += Math.Abs(sources[index].PixelData[offset] - decoded[index * frameBytes + offset]);

        Assert.That(total / (double)frameBytes, Is.LessThan(12d),
          $"ffmpeg's picture {index} is not the frame that was encoded");
      }
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }

  [Test]
  [Category("Unit")]
  public void ChangedReferencePictureUsesInterCavlcInsteadOfPcm() {
    var first = _Random420(32, 32, 101);
    var between = _Random420(32, 32, 102);
    var future = _Random420(32, 32, 103);
    var encoder = H264VideoEncoder.Create(_Stream(32, 32));
    var packets = new List<CodedPacket>();

    Assert.That(encoder.TryEncode(first, 0, out var idr), Is.True);
    packets.Add(idr);
    Assert.That(encoder.TryEncode(between, 1, out _), Is.False);
    Assert.That(encoder.TryEncode(future, 2, out var p), Is.True);
    packets.Add(p);
    packets.AddRange(encoder.Flush());

    var syntax = _FirstMacroblockSyntax(p, expectedSliceType: 5, referencePicture: true, bPicture: false);
    Assert.Multiple(() => {
      Assert.That(syntax.SkipRun, Is.Zero);
      Assert.That(syntax.MacroblockType, Is.Zero, "P_L0_16x16 must be used rather than P-slice I_PCM (mb_type 30)");
      Assert.That(syntax.CodedBlockPatternCodeNum, Is.Not.Null);
      Assert.That(syntax.CodedBlockPatternCodeNum, Is.Not.Zero,
        "a random changed macroblock must carry transform coefficients rather than falling back to PCM");
    });

    var decoded = _DecodeAll(encoder, packets);
    Assert.That(decoded, Has.Count.EqualTo(3));
    Assert.Multiple(() => {
      Assert.That(decoded[0].PixelData, Is.EqualTo(first.PixelData), "the IDR is lossless I_PCM");
      Assert.That(_AverageAbsoluteError(future.PixelData, decoded[2].PixelData), Is.LessThan(12d),
        "QP 18 inter reconstruction should stay close to the source while using CAVLC");
    });
  }

  [Test]
  [Category("Unit")]
  public void QuarterPelMotionSearchWritesExplicitPVector() {
    const int width = 32;
    const int height = 32;
    var reference = _Random420(width, height, 0x264);
    var shifted = _MotionPredicted420(reference, mvX: 3, mvY: -1);
    var encoder = H264VideoEncoder.Create(_Stream(width, height));

    Assert.That(encoder.TryEncode(reference, 0, out _), Is.True);
    Assert.That(encoder.TryEncode(reference, 1, out _), Is.False);
    Assert.That(encoder.TryEncode(shifted, 2, out var p), Is.True);

    var syntax = _FirstMacroblockSyntax(p, expectedSliceType: 5, referencePicture: true, bPicture: false);
    Assert.Multiple(() => {
      Assert.That(syntax.MacroblockType, Is.Zero);
      Assert.That(syntax.MvdL0X, Is.EqualTo(3), "the first macroblock predictor is zero, so MVD is the quarter-pel MV");
      Assert.That(syntax.MvdL0Y, Is.EqualTo(-1));
      Assert.That(syntax.CodedBlockPatternCodeNum, Is.Zero,
        "the constructed picture is exactly the normative fractional prediction and needs no residual");
    });
  }

  [Test]
  [Category("Unit")]
  public void BidirectionalPictureChoosesExplicitBiPredictionWhenItIsExact() {
    var past = _Solid420(16, 16, 0);
    var middle = _Solid420(16, 16, 128);
    var future = _Solid420(16, 16, 255);
    var encoder = H264VideoEncoder.Create(_Stream(16, 16));
    var packets = new List<CodedPacket>();

    Assert.That(encoder.TryEncode(past, 0, out var idr), Is.True);
    packets.Add(idr);
    Assert.That(encoder.TryEncode(middle, 1, out _), Is.False);
    Assert.That(encoder.TryEncode(future, 2, out var p), Is.True);
    packets.Add(p);
    packets.AddRange(encoder.Flush());
    var b = packets.Single(packet => packet.PresentationTimestamp == 1);

    var syntax = _FirstMacroblockSyntax(b, expectedSliceType: 6, referencePicture: false, bPicture: true);
    Assert.Multiple(() => {
      Assert.That(syntax.MacroblockType, Is.EqualTo(3), "B_Bi_16x16 should beat either single-list predictor");
      Assert.That(syntax.MvdL0X, Is.Zero);
      Assert.That(syntax.MvdL0Y, Is.Zero);
      Assert.That(syntax.MvdL1X, Is.Zero);
      Assert.That(syntax.MvdL1Y, Is.Zero);
      Assert.That(syntax.CodedBlockPatternCodeNum, Is.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void PeriodicGopBoundaryEmitsFreshIdrWithResetNumbering() {
    const int frames = 121;
    var source = _Solid420(16, 16, 90);
    var encoder = H264VideoEncoder.Create(_Stream(16, 16));
    var packets = new List<CodedPacket>();

    for (var index = 0; index < frames; ++index)
      if (encoder.TryEncode(source, index, out var packet))
        packets.Add(packet);
    packets.AddRange(encoder.Flush());

    Assert.That(packets, Has.Count.EqualTo(frames));
    var keys = packets.Where(packet => packet.IsKeyFrame).ToArray();
    Assert.That(keys.Select(packet => packet.PresentationTimestamp), Is.EqualTo(new long?[] { 0, 120 }));

    var reset = _SliceHeader(keys[1]);
    Assert.Multiple(() => {
      Assert.That(reset.FrameNum, Is.Zero);
      Assert.That(reset.PocLsb, Is.Zero);
      Assert.That(_SliceType(keys[1]), Is.EqualTo(7));
    });
  }

  [Test]
  [Category("Unit")]
  public void CavlcResidualWriterRoundTripsTheDecoder() {
    var cases = new[] {
      new int[16],
      new[] { 2, 0, -1, 0, 0, 3, 0, 0, 0, 0, -2, 1, 0, 0, 0, 0 },
      new[] { 1, -1, 2, -3, 1, 0, 0, 1, -1, 2, 0, 0, 0, 0, 1, -1 },
    };

    foreach (var nC in new[] { 0, 3, 6, 8 })
      foreach (var source in cases) {
        var writer = new H264BitWriter();
        var expectedCount = source.Count(value => value != 0);
        Assert.That(H264CavlcEncoding.WriteBlock(writer, source, nC, chromaDc: false), Is.EqualTo(expectedCount));

        var reader = new H264BitReader(writer.FinishRbsp());
        var decoded = new int[16];
        Assert.That(H264Residual.ReadBlock(ref reader, decoded, nC, chromaDc: false), Is.EqualTo(expectedCount));
        Assert.That(decoded, Is.EqualTo(source), $"nC={nC}");
      }

    var chromaSource = new[] { 2, -1, 0, 3 };
    var chromaWriter = new H264BitWriter();
    H264CavlcEncoding.WriteBlock(chromaWriter, chromaSource, -1, chromaDc: true);
    var chromaReader = new H264BitReader(chromaWriter.FinishRbsp());
    var chromaDecoded = new int[4];
    H264Residual.ReadBlock(ref chromaReader, chromaDecoded, -1, chromaDc: true);
    Assert.That(chromaDecoded, Is.EqualTo(chromaSource));
  }

  [Test]
  [Category("Unit")]
  public void TrailingPictureWithoutFutureAnchorFlushesAsP() {
    var first = _Random420(32, 16, 11);
    var second = _Random420(32, 16, 12);
    var encoder = H264VideoEncoder.Create(_Stream(32, 16));

    Assert.That(encoder.TryEncode(first, 0, out var idr), Is.True);
    Assert.That(encoder.TryEncode(second, 1, out _), Is.False);
    var trailing = encoder.Flush().Single();

    Assert.Multiple(() => {
      Assert.That(_SliceType(idr), Is.EqualTo(7));
      Assert.That(_SliceType(trailing), Is.EqualTo(5));
      Assert.That(trailing.PresentationTimestamp, Is.EqualTo(1));
      Assert.That(trailing.DecodeTimestamp, Is.EqualTo(1));
      Assert.That(_FirstMacroblockSyntax(
        trailing, expectedSliceType: 5, referencePicture: true, bPicture: false).MacroblockType, Is.Zero);
    });

    var decoded = _DecodeAll(encoder, [idr, trailing]);
    Assert.Multiple(() => {
      Assert.That(decoded, Has.Count.EqualTo(2));
      Assert.That(decoded[0].PixelData, Is.EqualTo(first.PixelData));
      Assert.That(_AverageAbsoluteError(second.PixelData, decoded[1].PixelData), Is.LessThan(12d));
    });
  }

  /// <summary>A bright square crossing a fixed background.</summary>
  private static RawImage _MovingSquare(int width, int height, int phase) {
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      var background = (byte)(40 + ((x / 8 + y / 8) & 1) * 30);
      data[at] = background;
      data[at + 1] = background;
      data[at + 2] = background;
    }

    var squareX = 4 + phase * 3;
    var squareY = 4 + phase;
    for (var y = squareY; y < Math.Min(squareY + 16, height); ++y)
    for (var x = squareX; x < Math.Min(squareX + 16, width); ++x) {
      var at = (y * width + x) * 3;
      data[at] = 230;
      data[at + 1] = 200;
      data[at + 2] = 60;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Unit")]
  public void EncoderWritesLengthPrefixedIdrThatOwnDecoderReadsExactly() {
    const int width = 18;
    const int height = 20;
    var frame = _Random420(width, height, 0x264);
    var encoder = H264VideoEncoder.Create(_Stream(width, height));

    Assert.That(encoder.TryEncode(frame, 7, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(7));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(7));
      Assert.That(packet.Data.Span[..4].ToArray(), Is.Not.EqualTo(new byte[] { 0, 0, 0, 1 }),
        "the codec emits AVC length-prefixed samples rather than Annex B");
    });

    var decoder = H264VideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out _), Is.False,
      "Main-profile pictures are held for POC presentation ordering until the stream is flushed");
    var decoded = decoder.Flush().Single();
    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Yuv420P8));
      Assert.That(decoded.PixelData, Is.EqualTo(frame.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void RawWriterTurnsEncoderOutputIntoAnnexBWithParameterSets() {
    var frame = _Random420(32, 16, 42);
    var encoder = H264VideoEncoder.Create(_Stream(frame.Width, frame.Height));
    encoder.TryEncode(frame, 0, out var packet);

    var annexB = VideoIO.Mux<H264VideoWriter>([encoder.DescribeStream()], [packet]);
    var units = H264NalReader.SplitAnnexB(annexB).ToArray();

    Assert.That(units.Select(unit => unit.Type), Is.EqualTo(new[] {
      H264NalUnitType.SequenceParameterSet,
      H264NalUnitType.PictureParameterSet,
      H264NalUnitType.IdrSlice,
    }));

    var decoder = H264VideoDecoder.Create(_Stream(frame.Width, frame.Height));
    Assert.That(decoder.TryDecode(new(0, annexB), out _), Is.False);
    var decoded = decoder.Flush().Single();
    Assert.That(decoded.PixelData, Is.EqualTo(frame.PixelData));
  }

  [TestCase(17, 16)]
  [TestCase(16, 17)]
  [Category("Unit")]
  public void Odd420DimensionsAreRefused(int width, int height)
    => Assert.That(
      () => H264VideoEncoder.Create(_Stream(width, height)),
      Throws.TypeOf<NotSupportedException>().With.Message.Contains("Both dimensions must be even"));

  [Test]
  [Category("Unit")]
  public void RegistryExposesH264Encoder() {
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(16, 16));
    Assert.That(encoder, Is.TypeOf<H264VideoEncoder>());
  }

  private static int _SliceType(CodedPacket packet) {
    var nal = H264NalReader.SplitLengthPrefixed(packet.Data, 4).Single();
    var reader = new H264BitReader(nal.Payload);
    reader.ReadUnsignedExpGolomb();
    return reader.ReadUnsignedExpGolomb();
  }

  private static FirstMacroblockSyntax _FirstMacroblockSyntax(
    CodedPacket packet,
    int expectedSliceType,
    bool referencePicture,
    bool bPicture) {
    var nal = H264NalReader.SplitLengthPrefixed(packet.Data, 4).Single();
    var reader = new H264BitReader(nal.Payload);
    Assert.That(reader.ReadUnsignedExpGolomb(), Is.Zero); // first_mb_in_slice
    Assert.That(reader.ReadUnsignedExpGolomb(), Is.EqualTo(expectedSliceType));
    Assert.That(reader.ReadUnsignedExpGolomb(), Is.Zero); // pps id
    var frameNum = reader.ReadBits(16);
    if (nal.IsIdr)
      reader.ReadUnsignedExpGolomb();
    var pocLsb = reader.ReadBits(16);
    if (bPicture)
      Assert.That(reader.ReadBit(), Is.EqualTo(1)); // direct_spatial_mv_pred_flag
    if (expectedSliceType % 5 is 0 or 1) {
      Assert.That(reader.ReadBit(), Is.Zero); // active-reference override
      Assert.That(reader.ReadBit(), Is.Zero); // list0 modification
      if (bPicture)
        Assert.That(reader.ReadBit(), Is.Zero); // list1 modification
    }
    if (nal.IsIdr) {
      reader.Skip(2);
    } else if (referencePicture)
      Assert.That(reader.ReadBit(), Is.Zero); // adaptive reference marking
    Assert.That(reader.ReadSignedExpGolomb(), Is.Zero); // slice_qp_delta
    Assert.That(reader.ReadUnsignedExpGolomb(), Is.EqualTo(1)); // deblocking disabled

    var skipRun = reader.ReadUnsignedExpGolomb();
    if (skipRun != 0)
      return new(frameNum, pocLsb, skipRun, null, null, null, null, null, null);

    var mbType = reader.ReadUnsignedExpGolomb();
    int? mvdL0X = null;
    int? mvdL0Y = null;
    int? mvdL1X = null;
    int? mvdL1Y = null;
    int? codedBlockPatternCodeNum = null;

    if (!bPicture && mbType == 0) {
      mvdL0X = reader.ReadSignedExpGolomb();
      mvdL0Y = reader.ReadSignedExpGolomb();
      codedBlockPatternCodeNum = reader.ReadUnsignedExpGolomb();
    } else if (bPicture && mbType is >= 1 and <= 3) {
      if (mbType is 1 or 3) {
        mvdL0X = reader.ReadSignedExpGolomb();
        mvdL0Y = reader.ReadSignedExpGolomb();
      }
      if (mbType is 2 or 3) {
        mvdL1X = reader.ReadSignedExpGolomb();
        mvdL1Y = reader.ReadSignedExpGolomb();
      }
      codedBlockPatternCodeNum = reader.ReadUnsignedExpGolomb();
    }

    return new(
      frameNum, pocLsb, skipRun, mbType,
      mvdL0X, mvdL0Y, mvdL1X, mvdL1Y, codedBlockPatternCodeNum);
  }

  private static SliceHeaderSyntax _SliceHeader(CodedPacket packet) {
    var nal = H264NalReader.SplitLengthPrefixed(packet.Data, 4).Single();
    var reader = new H264BitReader(nal.Payload);
    reader.ReadUnsignedExpGolomb();
    var sliceType = reader.ReadUnsignedExpGolomb();
    reader.ReadUnsignedExpGolomb();
    var frameNum = reader.ReadBits(16);
    if (nal.IsIdr)
      reader.ReadUnsignedExpGolomb();
    var pocLsb = reader.ReadBits(16);
    return new(sliceType, frameNum, pocLsb);
  }

  private static List<RawImage> _DecodeAll(H264VideoEncoder encoder, IEnumerable<CodedPacket> packets) {
    var decoder = H264VideoDecoder.Create(encoder.DescribeStream());
    var decoded = new List<RawImage>();
    foreach (var packet in packets)
      if (decoder.TryDecode(packet, out var frame))
        decoded.Add(frame);
    decoded.AddRange(decoder.Flush());
    return decoded;
  }

  private static double _AverageAbsoluteError(byte[] expected, byte[] actual) {
    Assert.That(actual, Has.Length.EqualTo(expected.Length));
    var total = 0L;
    for (var i = 0; i < expected.Length; ++i)
      total += Math.Abs(expected[i] - actual[i]);
    return total / (double)expected.Length;
  }

  private static RawImage _MotionPredicted420(RawImage reference, int mvX, int mvY) {
    var width = reference.Width;
    var height = reference.Height;
    var lumaSamples = width * height;
    var chromaWidth = width / 2;
    var chromaHeight = height / 2;
    var chromaSamples = chromaWidth * chromaHeight;
    var source = reference.PixelData;
    var y = source.AsSpan(0, lumaSamples).ToArray();
    var cb = source.AsSpan(lumaSamples, chromaSamples).ToArray();
    var cr = source.AsSpan(lumaSamples + chromaSamples, chromaSamples).ToArray();
    var result = new byte[source.Length];

    H264MotionCompensation.PredictLuma(
      y, width, height, 0, 0, mvX, mvY, width, height, result.AsSpan(0, lumaSamples));
    H264MotionCompensation.PredictChroma(
      cb, chromaWidth, chromaHeight, 0, 0, mvX, mvY, chromaWidth, chromaHeight,
      result.AsSpan(lumaSamples, chromaSamples));
    H264MotionCompensation.PredictChroma(
      cr, chromaWidth, chromaHeight, 0, 0, mvX, mvY, chromaWidth, chromaHeight,
      result.AsSpan(lumaSamples + chromaSamples, chromaSamples));

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Yuv420P8,
      PixelData = result,
      ColorInfo = RawImageColorInfo.Bt601Limited,
    };
  }

  private static RawImage _Solid420(int width, int height, byte value) {
    var pixels = new byte[width * height * 3 / 2];
    pixels.AsSpan().Fill(value);
    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Yuv420P8,
      PixelData = pixels,
      ColorInfo = RawImageColorInfo.Bt601Limited,
    };
  }

  private readonly record struct FirstMacroblockSyntax(
    int FrameNum,
    int PocLsb,
    int SkipRun,
    int? MacroblockType,
    int? MvdL0X,
    int? MvdL0Y,
    int? MvdL1X,
    int? MvdL1Y,
    int? CodedBlockPatternCodeNum);

  private readonly record struct SliceHeaderSyntax(int SliceType, int FrameNum, int PocLsb);

  private static MediaStreamInfo _Stream(int width, int height)
    => new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("avc1"),
      TimeBase = new Rational(1, 25),
      FrameRate = new Rational(25, 1),
      Width = width,
      Height = height,
      BitsPerPixel = 12,
    };

  private static RawImage _Random420(int width, int height, int seed) {
    var pixels = new byte[width * height * 3 / 2];
    new Random(seed).NextBytes(pixels);
    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Yuv420P8,
      PixelData = pixels,
      ColorInfo = RawImageColorInfo.Bt601Limited,
    };
  }
}
