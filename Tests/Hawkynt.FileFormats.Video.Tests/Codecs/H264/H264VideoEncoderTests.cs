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
  public void RepeatedPicturesUsePSkipAndBidirectionalBPredictionAndRoundTripExactly() {
    const int width = 32;
    const int height = 32;
    const int frames = 5;
    var source = _Random420(width, height, 0x264);
    var encoder = H264VideoEncoder.Create(_Stream(width, height));
    var packets = new List<CodedPacket>();

    for (var index = 0; index < frames; ++index)
      if (encoder.TryEncode(source, index, out var packet))
        packets.Add(packet);
    packets.AddRange(encoder.Flush());

    Assert.Multiple(() => {
      Assert.That(packets, Has.Count.EqualTo(frames));
      Assert.That(packets.Select(_SliceType), Is.EqualTo(new[] { 7, 5, 6, 5, 6 }));
      Assert.That(packets.Select(packet => packet.IsKeyFrame),
        Is.EqualTo(new[] { true, false, false, false, false }));
      Assert.That(packets.Select(packet => packet.PresentationTimestamp),
        Is.EqualTo(new long?[] { 0, 2, 1, 4, 3 }));
      Assert.That(packets.Select(packet => packet.DecodeTimestamp),
        Is.EqualTo(new long?[] { 0, 1, 2, 3, 4 }));
    });

    var pSyntax = _FirstMacroblockSyntax(packets[1], expectedSliceType: 5, referencePicture: true, bPicture: false);
    Assert.That(pSyntax.SkipRun, Is.EqualTo(4),
      "the 32x32 repeated P picture should be four P_Skip macroblocks, not four intra fallbacks");

    var bSyntax = _FirstMacroblockSyntax(packets[2], expectedSliceType: 6, referencePicture: false, bPicture: true);
    Assert.Multiple(() => {
      Assert.That(bSyntax.SkipRun, Is.Zero, "the first B macroblock is explicitly coded, not direct-skip");
      Assert.That(bSyntax.MacroblockType, Is.EqualTo(3),
        "the repeated B picture should use B_Bi_16x16 and therefore both reference lists");
    });

    var decoder = H264VideoDecoder.Create(encoder.DescribeStream());
    var decoded = new List<RawImage>();
    foreach (var packet in packets)
      if (decoder.TryDecode(packet, out var frame))
        decoded.Add(frame);
    decoded.AddRange(decoder.Flush());

    Assert.That(decoded, Has.Count.EqualTo(frames));
    for (var index = 0; index < frames; ++index)
      Assert.Multiple(() => {
        Assert.That(decoded[index].Format, Is.EqualTo(PixelFormat.Yuv420P8));
        Assert.That(decoded[index].PixelData, Is.EqualTo(source.PixelData), $"display picture {index}");
      });
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
    });

    var decoder = H264VideoDecoder.Create(encoder.DescribeStream());
    decoder.TryDecode(idr, out _);
    decoder.TryDecode(trailing, out _);
    var decoded = decoder.Flush().ToArray();
    Assert.That(decoded.Select(frame => frame.PixelData), Is.EqualTo(new[] { first.PixelData, second.PixelData }));
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
    reader.Skip(16); // frame_num
    if (nal.IsIdr)
      reader.ReadUnsignedExpGolomb();
    reader.Skip(16); // pic_order_cnt_lsb
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
    return new(skipRun, skipRun == 0 ? reader.ReadUnsignedExpGolomb() : null);
  }

  private readonly record struct FirstMacroblockSyntax(int SkipRun, int? MacroblockType);

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
