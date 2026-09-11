using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Mpeg4.Tests;

/// <summary>The MPEG-4 Part 2 writer against the decoder beside it and the public codec registry.</summary>
[TestFixture]
public sealed class Mpeg4VideoEncoderTests {

  [TestCase(16, 16)]
  [TestCase(64, 48)]
  [TestCase(127, 95)]
  [Category("Unit")]
  public void RectangularPictureSizesAreAccepted(int width, int height) {
    var stream = Mpeg4VideoEncoder.Create(_Stream(width, height)).DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Width, Is.EqualTo(width));
      Assert.That(stream.Height, Is.EqualTo(height));
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("mp4v")));
    });
  }

  [TestCase(0, 16)]
  [TestCase(16, 0)]
  [TestCase(8192, 16)]
  [TestCase(16, 8192)]
  [Category("Unit")]
  public void DimensionsTheVolCannotStateAreRefused(int width, int height) {
    var refusal = Assert.Throws<NotSupportedException>(() => Mpeg4VideoEncoder.Create(_Stream(width, height)));

    Assert.That(refusal!.Message, Does.Contain($"{width}x{height}"));
  }

  [Test]
  [Category("Unit")]
  public void AnAudioStreamIsRefused() {
    var refusal = Assert.Throws<NotSupportedException>(() => Mpeg4VideoEncoder.Create(new() {
      Index = 0,
      Kind = MediaStreamKind.Audio,
      Codec = CodecTag.FromCharacters("mp4v"),
      Width = 16,
      Height = 16,
    }));

    Assert.That(refusal!.Message, Does.Contain("video"));
  }

  [Test]
  [Category("Unit")]
  public void AChangedPictureSizeIsRefused() {
    var encoder = Mpeg4VideoEncoder.Create(_Stream(32, 32));
    var refusal = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Picture(48, 32, 0), 0, out _));

    Assert.That(refusal!.Message, Does.Contain("48x32"));
  }

  [Test]
  [Category("Unit")]
  public void TruncatedSourcePixelsAreRefusedBeforeTheConverterReadsThem() {
    var encoder = Mpeg4VideoEncoder.Create(_Stream(32, 32));
    var frame = new RawImage { Width = 32, Height = 32, Format = PixelFormat.Rgb24, PixelData = [1, 2, 3] };

    var refusal = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(frame, 0, out _));

    Assert.That(refusal!.Message, Does.Contain("enough pixel data"));
  }

  [Test]
  [Category("Unit")]
  public void PacketsAreReorderedIntoIPBBGroups() {
    var encoder = Mpeg4VideoEncoder.Create(_Stream(32, 32));
    var packets = new List<CodedPacket>();

    for (var index = 0; index < 7; ++index)
      if (encoder.TryEncode(_Picture(32, 32, index), index, out var packet))
        packets.Add(packet);

    packets.AddRange(encoder.Flush());

    Assert.That(packets, Has.Count.EqualTo(7));
    Assert.Multiple(() => {
      Assert.That(packets.Select(_VopType).ToArray(), Is.EqualTo(new[] {
        Mpeg4VideoObjectPlane.IntraCoded,
        Mpeg4VideoObjectPlane.PredictiveCoded,
        Mpeg4VideoObjectPlane.BidirectionallyCoded,
        Mpeg4VideoObjectPlane.BidirectionallyCoded,
        Mpeg4VideoObjectPlane.PredictiveCoded,
        Mpeg4VideoObjectPlane.BidirectionallyCoded,
        Mpeg4VideoObjectPlane.BidirectionallyCoded,
      }));
      Assert.That(packets.Select(packet => packet.PresentationTimestamp).ToArray(),
        Is.EqualTo(new long?[] { 0, 3, 1, 2, 6, 4, 5 }));
      Assert.That(packets.Select(packet => packet.DecodeTimestamp).ToArray(),
        Is.EqualTo(new long?[] { 0, 1, 2, 3, 4, 5, 6 }));
      Assert.That(packets.Select(packet => packet.IsKeyFrame).ToArray(),
        Is.EqualTo(new[] { true, false, false, false, false, false, false }));
      Assert.That(packets.All(packet => _ContainsStartCode(packet.Data.Span, Mpeg4StartCode.FirstVideoObjectLayer)), Is.True);
      Assert.That(packets.All(packet => _ContainsStartCode(packet.Data.Span, Mpeg4StartCode.VideoObjectPlane)), Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void TheTwelfthDisplayFrameStartsANewIntraGroup() {
    var encoder = Mpeg4VideoEncoder.Create(_Stream(32, 32));
    var packets = new List<CodedPacket>();

    for (var index = 0; index <= 12; ++index)
      if (encoder.TryEncode(_Picture(32, 32, index), index, out var packet))
        packets.Add(packet);

    packets.AddRange(encoder.Flush());

    var secondIntra = packets.Single(packet => packet.PresentationTimestamp == 12);
    Assert.Multiple(() => {
      Assert.That(_VopType(secondIntra), Is.EqualTo(Mpeg4VideoObjectPlane.IntraCoded));
      Assert.That(secondIntra.IsKeyFrame, Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void WhatTheEncoderWritesIsWhatTheDecoderReadsInDisplayOrder() {
    const int width = 64;
    const int height = 48;
    const int frames = 8;
    const double worstMeanSquaredError = 140d;

    var encoder = Mpeg4VideoEncoder.Create(_Stream(width, height));
    var packets = new List<CodedPacket>();
    var sources = new List<RawImage>();

    for (var index = 0; index < frames; ++index) {
      var source = _Picture(width, height, index);
      sources.Add(source);
      if (encoder.TryEncode(source, index, out var packet))
        packets.Add(packet);
    }
    packets.AddRange(encoder.Flush());

    Assert.That(packets, Has.Count.EqualTo(frames));

    var decoder = Mpeg4VideoDecoder.Create(encoder.DescribeStream());
    var decoded = new List<RawImage>();
    foreach (var packet in packets)
      if (decoder.TryDecode(packet, out var frame))
        decoded.Add(frame);
    decoded.AddRange(decoder.Flush());

    Assert.That(decoded, Has.Count.EqualTo(frames));
    for (var index = 0; index < frames; ++index) {
      Assert.Multiple(() => {
        Assert.That(decoded[index].Width, Is.EqualTo(width));
        Assert.That(decoded[index].Height, Is.EqualTo(height));
        Assert.That(decoded[index].Format, Is.EqualTo(PixelFormat.Rgb24));
      });

      var error = _MeanSquaredError(sources[index].PixelData, decoded[index].PixelData);
      Assert.That(error, Is.LessThan(worstMeanSquaredError),
        $"frame {index} came back {error:F1} squared levels from what went in");
    }
  }

  [Test]
  [Category("Conformance")]
  public void FFmpegReadsEveryReorderedPicture() {
    FFmpegOracle.RequireAvailable();

    const int width = 64;
    const int height = 48;
    const int frames = 8;
    var encoder = Mpeg4VideoEncoder.Create(_Stream(width, height));
    var packets = new List<CodedPacket>();

    for (var index = 0; index < frames; ++index)
      if (encoder.TryEncode(_Picture(width, height, index), index, out var packet))
        packets.Add(packet);
    packets.AddRange(encoder.Flush());

    var avi = VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets);
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");

    try {
      File.WriteAllBytes(path, avi);
      var (decoded, detail) = FFmpegOracle.TryDecodeFrameCount(path, width, height, frames);
      Assert.That(decoded, Is.True, detail);
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }
  }

  [Test]
  [Category("Conformance")]
  public void FFmpegReadsMovingContentBackAsTheFramesThatWentIn() {
    // Counting the pictures back proves the reordering; it does not prove the vectors. A vector
    // coded against the wrong predictor, folded the wrong way, or halved for chrominance with the
    // wrong operator still yields the right number of pictures -- just not the right ones. So this
    // gives the encoder content that genuinely moves and compares every decoded frame.
    FFmpegOracle.RequireAvailable();

    const int width = 128;
    const int height = 96;
    const int frames = 24; // Two whole groups, so a group boundary is crossed as well.

    var encoder = Mpeg4VideoEncoder.Create(_Stream(width, height));
    var packets = new List<CodedPacket>();
    var sources = new List<RawImage>();

    for (var index = 0; index < frames; ++index) {
      var picture = _MovingSquare(width, height, index);
      sources.Add(picture);
      if (encoder.TryEncode(picture, index, out var packet))
        packets.Add(packet);
    }

    packets.AddRange(encoder.Flush());

    var directory = Directory.CreateTempSubdirectory("mpeg4-oracle");
    try {
      var path = Path.Combine(directory.FullName, "clip.avi");
      File.WriteAllBytes(path, VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets));

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

      // Every frame, not an average: a mispredicted vector shows up as one bad picture among good
      // ones, and B-VOPs make it likelier to be one in the middle than one at either end.
      for (var index = 0; index < frames; ++index) {
        var total = 0L;
        for (var offset = 0; offset < frameBytes; ++offset)
          total += Math.Abs(sources[index].PixelData[offset] - decoded[index * frameBytes + offset]);

        Assert.That(total / (double)frameBytes, Is.LessThan(14d),
          $"ffmpeg's picture {index} is not the frame that was encoded");
      }
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }

  [Test]
  [Category("RoundTrip")]
  public void SearchingForMotionBeatsAssumingThereIsNone() {
    // A square moving three samples a frame is motion a search finds and a zero vector cannot. The
    // comparison is against the same encoder denied the search -- a still scene, where a zero vector
    // is the right answer -- so what it measures is the search doing its job rather than prediction
    // in general.
    const int width = 128;
    const int height = 96;

    var moving = Mpeg4VideoEncoder.Create(_Stream(width, height));
    var movingBytes = 0;
    for (var index = 0; index < 12; ++index)
      if (moving.TryEncode(_MovingSquare(width, height, index), index, out var packet))
        movingBytes += packet.Data.Length;

    foreach (var packet in moving.Flush())
      movingBytes += packet.Data.Length;

    // The baseline is the same encoder denied prediction entirely: a fresh encoder per frame, so
    // every frame is the first of its group and therefore intra. That is a real number this codec
    // produces rather than a bound picked by hand, and it is what prediction has to beat to be worth
    // having at all.
    var intraBytes = 0;
    for (var index = 0; index < 12; ++index) {
      var single = Mpeg4VideoEncoder.Create(_Stream(width, height));
      if (single.TryEncode(_MovingSquare(width, height, index), index, out var only))
        intraBytes += only.Data.Length;
    }

    Assert.That(movingBytes, Is.LessThan(intraBytes),
      $"twelve predicted pictures cost {movingBytes} bytes against {intraBytes} coded as intra pictures");
  }

  [Test]
  [Category("RoundTrip")]
  public void AStillSceneCollapsesToUncodedMacroblocks() {
    // What not_coded is worth, isolated from the quantiser and from the motion search. The first
    // predicted picture still costs something -- it corrects the intra picture's own quantisation
    // error -- but once that correction is in the anchor there is nothing left to say, and a
    // macroblock that is not coded in the anchor is not carried by the B-VOPs around it either.
    const int width = 128;
    const int height = 96;
    var encoder = Mpeg4VideoEncoder.Create(_Stream(width, height));
    var picture = _MovingSquare(width, height, 0);
    var sizes = new List<int>();

    for (var frame = 0; frame < 12; ++frame)
      if (encoder.TryEncode(picture, frame, out var packet))
        sizes.Add(packet.Data.Length);

    foreach (var packet in encoder.Flush())
      sizes.Add(packet.Data.Length);

    Assert.That(sizes[^1], Is.LessThan(sizes[0] / 4d),
      $"a settled picture is {sizes[^1]} bytes against {sizes[0]} for the intra one; "
      + $"the run was {string.Join(", ", sizes)}");
  }

  /// <summary>A bright square crossing a fixed background, which is motion and nothing else.</summary>
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
    var squareY = 8 + phase;
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
  public void AShortTailIsFlushedAsPredictedPicturesRatherThanDropped() {
    var encoder = Mpeg4VideoEncoder.Create(_Stream(32, 32));

    Assert.That(encoder.TryEncode(_Picture(32, 32, 0), 0, out _), Is.True);
    Assert.That(encoder.TryEncode(_Picture(32, 32, 1), 1, out _), Is.False);
    Assert.That(encoder.TryEncode(_Picture(32, 32, 2), 2, out _), Is.False);

    var tail = encoder.Flush().ToArray();
    Assert.Multiple(() => {
      Assert.That(tail, Has.Length.EqualTo(2));
      Assert.That(tail.Select(_VopType).ToArray(),
        Is.EqualTo(new[] { Mpeg4VideoObjectPlane.PredictiveCoded, Mpeg4VideoObjectPlane.PredictiveCoded }));
      Assert.That(tail.Select(packet => packet.PresentationTimestamp).ToArray(), Is.EqualTo(new long?[] { 1, 2 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheStreamDescriptionIsVfwMp4vAndTheDecoderAcceptsIt() {
    var stream = Mpeg4VideoEncoder.Create(_Stream(64, 48)).DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("mp4v")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("mp4v")));
      Assert.That(stream.CodecId, Is.EqualTo("V_MS/VFW/FOURCC"));
      Assert.That(stream.CodecPrivateData.Length, Is.EqualTo(40));
      Assert.That(Mpeg4VideoDecoder.Accepts(stream), Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void TheRegistryReachesBothHalvesOfMpeg4PartTwo() {
    var stream = _Stream(64, 48);

    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
      Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True);
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<Mpeg4VideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<Mpeg4VideoEncoder>());
    });
  }

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("mp4v"),
    TimeBase = new Rational(1001, 30000),
    FrameRate = new Rational(30000, 1001),
    Width = width,
    Height = height,
  };

  /// <summary>
  /// A greyscale texture. Constant chrominance keeps the error measured here on the transform and
  /// quantiser rather than on a choice of 4:2:0 upsampling convention.
  /// </summary>
  private static RawImage _Picture(int width, int height, int frame) {
    var pixels = new byte[width * height * 3];
    var left = frame * 5 % Math.Max(1, width - 16);
    var top = frame * 3 % Math.Max(1, height - 16);

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var inside = x >= left && x < left + 16 && y >= top && y < top + 16;
        var value = (byte)(inside ? 216 : 32 + ((x >> 2) * 7 + (y >> 2) * 11) % 144);
        var at = (y * width + x) * 3;
        pixels[at] = value;
        pixels[at + 1] = value;
        pixels[at + 2] = value;
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static int _VopType(CodedPacket packet) {
    var bytes = packet.Data.Span;
    for (var i = 0; i + 4 < bytes.Length; ++i)
      if (bytes[i] == 0 && bytes[i + 1] == 0 && bytes[i + 2] == 1 && bytes[i + 3] == Mpeg4StartCode.VideoObjectPlane)
        return bytes[i + 4] >> 6;

    Assert.Fail("packet has no MPEG-4 VOP start code");
    return -1;
  }

  private static bool _ContainsStartCode(ReadOnlySpan<byte> bytes, byte code) {
    for (var i = 0; i + 3 < bytes.Length; ++i)
      if (bytes[i] == 0 && bytes[i + 1] == 0 && bytes[i + 2] == 1 && bytes[i + 3] == code)
        return true;

    return false;
  }

  private static double _MeanSquaredError(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual) {
    Assert.That(actual.Length, Is.EqualTo(expected.Length));
    long squared = 0;
    for (var i = 0; i < expected.Length; ++i) {
      var difference = expected[i] - actual[i];
      squared += difference * difference;
    }

    return (double)squared / expected.Length;
  }
}
