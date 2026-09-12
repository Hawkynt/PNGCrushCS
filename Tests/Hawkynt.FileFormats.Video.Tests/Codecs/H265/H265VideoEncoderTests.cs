using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using FileFormat.Codecs;
using FileFormat.Core;
using FileFormat.Matroska;
using Hawkynt.FileFormats.Video.Tests;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.H265.Tests;

[TestFixture]
public sealed class H265VideoEncoderTests {

  [Test]
  [Category("Oracle")]
  public void FFmpegReadsEveryPictureBackAsTheFrameThatWentIn() {
    // Long enough to cross a group boundary, so the clip carries a key picture, eleven predicted
    // pictures, a second key picture and more predicted ones after it. Every frame is compared, not
    // only the first: a decoder given a broken predicted picture still produces a picture, and the
    // damage shows up as drift that accumulates over the group rather than as a failure to decode.
    // This is the assertion that would have caught a motion vector written against the wrong
    // predictor, a residual coded in the wrong scan, or a reconstruction that differs from the
    // decoder's by a rounding step.
    FFmpegOracle.RequireAvailable();

    const int width = 64;
    const int height = 48;
    const int frames = 24;

    var encoder = H265VideoEncoder.Create(_Stream(width, height));
    var packets = new List<CodedPacket>();
    var sources = new List<RawImage>();

    for (var index = 0; index < frames; ++index) {
      var picture = _MovingSquare(width, height, index);
      sources.Add(picture);
      if (encoder.TryEncode(picture, index, out var packet))
        packets.Add(packet);
    }

    // Nothing is held back: no picture is coded out of order, so there is no Flush to drain.
    Assert.That(packets, Has.Count.EqualTo(frames));
    Assert.That(packets.FindAll(static packet => !packet.IsKeyFrame), Is.Not.Empty,
      "a clip of only key pictures would pass everything below without testing prediction at all");

    var directory = Directory.CreateTempSubdirectory("h265-oracle");
    try {
      var path = Path.Combine(directory.FullName, "clip.mkv");
      File.WriteAllBytes(path, VideoIO.Mux<MatroskaWriter>([encoder.DescribeStream()], packets));

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


  private static MediaStreamInfo _Stream(int width = 64, int height = 48) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("hvc1"),
    Handler = CodecTag.FromCharacters("hvc1"),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _Picture(int width = 64, int height = 48, int phase = 0) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)((x * 5 + phase * 17) & 0xFF);
      pixels[at + 1] = (byte)((y * 7 + phase * 29) & 0xFF);
      pixels[at + 2] = (byte)(((x ^ y) * 11 + phase * 43) & 0xFF);
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  [Test]
  [Category("Unit")]
  public void RegistryCreatesTheHevcEncoder() {
    var stream = _Stream();
    Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<H265VideoEncoder>());
  }

  [Test]
  [Category("Unit")]
  public void StreamDescriptionNeedsTheFirstPicturesParameterSets() {
    var encoder = H265VideoEncoder.Create(_Stream());
    Assert.That(() => encoder.DescribeStream(), Throws.InvalidOperationException);
  }

  [Test]
  [Category("Unit")]
  public void WritesMainProfileUniformPcmThatTheVideoDecoderReads() {
    var source = _Picture();
    var encoder = H265VideoEncoder.Create(_Stream());

    Assert.That(encoder.TryEncode(source, 7, out var packet), Is.True);
    var stream = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("hvc1")));
      Assert.That(stream.CodecId, Is.EqualTo("V_MPEGH/ISO/HEVC"));
      Assert.That(stream.CodecPrivateData.IsEmpty, Is.False);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(7));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(7));
      Assert.That(packet.IsKeyFrame, Is.True);
    });

    var configuration = H265DecoderConfiguration.TryParse(stream.CodecPrivateData);
    Assert.That(configuration, Is.Not.Null);

    H265SequenceParameterSet? sps = null;
    foreach (var bytes in configuration!.ParameterSets) {
      var nal = H265NalReader.Parse(bytes);
      if (nal.Type == H265NalUnitType.SequenceParameterSet)
        sps = H265SequenceParameterSet.Parse(nal.Payload);
    }

    Assert.That(sps, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(sps!.ProfileTierLevel.ProfileIdc, Is.EqualTo(H265ProfileTierLevel.MAIN));
      Assert.That(sps.ProfileTierLevel.IsMainCompatible, Is.True);
      Assert.That(sps.PcmEnabled, Is.True);
      Assert.That(sps.ChromaFormatIdc, Is.EqualTo(1));
      Assert.That(sps.BitDepthLuma, Is.EqualTo(8));
      Assert.That(sps.BitDepthChroma, Is.EqualTo(8));
    });

    var decoder = H265VideoDecoder.Create(stream);
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);

    var expected = FastRawImageConverter.Convert(
      FastRawImageConverter.Convert(source, PixelFormat.Yuv420P8), PixelFormat.Rgb24);
    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(source.Width));
      Assert.That(decoded.Height, Is.EqualTo(source.Height));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(decoded.PixelData, Is.EqualTo(expected.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void AGroupOpensWithAKeyPictureAndTheRestArePredicted() {
    var encoder = H265VideoEncoder.Create(_Stream());
    var packets = new List<CodedPacket>();

    for (var index = 0; index < 14; ++index)
      if (encoder.TryEncode(_Picture(phase: index), index, out var packet))
        packets.Add(packet);

    Assert.Multiple(() => {
      Assert.That(packets[0].IsKeyFrame, Is.True, "a stream has to open with a picture a decoder can start at");
      for (var index = 1; index < 12; ++index)
        Assert.That(packets[index].IsKeyFrame, Is.False, $"picture {index} is predicted");

      Assert.That(packets[12].IsKeyFrame, Is.True, "the next group opens with a key picture of its own");

      // The point of predicting: a picture that states a difference is a fraction of the size of one
      // that states every sample. A predicted picture that grew to the size of the key picture would
      // mean the prediction was not being used, and would pass every other assertion here.
      var predicted = packets[1].Data.Length;
      Assert.That(predicted, Is.LessThan(packets[0].Data.Length / 4),
        $"a predicted picture is {predicted} bytes against the key picture's {packets[0].Data.Length}");
    });
  }

  [Test]
  [Category("Unit")]
  public void TheParameterSetsDoNotChangeWithinAStream() {
    var encoder = H265VideoEncoder.Create(_Stream());

    Assert.That(encoder.TryEncode(_Picture(phase: 1), 0, out _), Is.True);
    var first = encoder.DescribeStream().CodecPrivateData.ToArray();
    Assert.That(encoder.TryEncode(_Picture(phase: 2), 1, out _), Is.True);

    Assert.That(encoder.DescribeStream().CodecPrivateData.ToArray(), Is.EqualTo(first));
  }

  [Test]
  [Category("Unit")]
  public void RefusesOddGeometryRatherThanChangingTheDisplaySize() {
    Assert.That(() => H265VideoEncoder.Create(_Stream(63, 48)),
      Throws.TypeOf<NotSupportedException>().With.Message.Contains("even dimensions"));
    Assert.That(() => H265VideoEncoder.Create(_Stream(64, 47)),
      Throws.TypeOf<NotSupportedException>().With.Message.Contains("even dimensions"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAFrameWhoseGeometryChanges() {
    var encoder = H265VideoEncoder.Create(_Stream());
    Assert.That(() => encoder.TryEncode(_Picture(32, 48), 0, out _),
      Throws.TypeOf<InvalidDataException>().With.Message.Contains("64x48"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesTruncatedSourceSamples() {
    var encoder = H265VideoEncoder.Create(_Stream());
    var full = _Picture();
    var source = new RawImage {
      Width = full.Width,
      Height = full.Height,
      Format = full.Format,
      PixelData = full.PixelData[..^1],
    };

    Assert.That(() => encoder.TryEncode(source, 0, out _),
      Throws.TypeOf<InvalidDataException>().With.Message.Contains("needs"));
  }
}
