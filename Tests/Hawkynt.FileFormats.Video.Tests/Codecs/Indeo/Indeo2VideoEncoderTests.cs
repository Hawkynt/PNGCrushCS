using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Codecs.Indeo;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class Indeo2VideoEncoderTests {

  [Test]
  [Category("Unit")]
  public void TheEncoderIsRegisteredUnderRt21() {
    var stream = _Stream(8, 4);

    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.AllEncoders.Select(e => e.CodecName), Does.Contain("Intel Indeo 2"));
      Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True);
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<Indeo2VideoEncoder>());
    });
  }

  [Test]
  [Category("Unit")]
  public void DescribeStreamWritesTheVfwRt21Description() {
    var requested = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("RT21"),
      Width = 8,
      Height = 4,
      TimeBase = new Rational(1, 25),
      FrameRate = new Rational(25, 1),
      DeclaredFrameCount = 7,
    };

    var described = Indeo2VideoEncoder.Create(requested).DescribeStream();

    Assert.Multiple(() => {
      Assert.That(described.Codec, Is.EqualTo(CodecTag.FromCharacters("RT21")));
      Assert.That(described.Handler, Is.EqualTo(CodecTag.FromCharacters("RT21")));
      Assert.That(described.CodecId, Is.EqualTo("V_MS/VFW/FOURCC"));
      Assert.That(described.BitsPerPixel, Is.EqualTo(24));
      Assert.That(described.TimeBase, Is.EqualTo(requested.TimeBase));
      Assert.That(described.FrameRate, Is.EqualTo(requested.FrameRate));
      Assert.That(described.DeclaredFrameCount, Is.EqualTo(7));
      Assert.That(described.CodecPrivateData, Has.Length.EqualTo(40));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(described.CodecPrivateData.Span[14..]), Is.EqualTo(24));
      Assert.That(described.CodecPrivateData.Slice(16, 4).ToArray(), Is.EqualTo("RT21"u8.ToArray()));
    });
  }

  [TestCase(7, 4)]
  [TestCase(8, 3)]
  [Category("Unit")]
  public void GeometryTheNativePlanesCannotRepresentRefuses(int width, int height)
    => Assert.Throws<NotSupportedException>(() => Indeo2VideoEncoder.Create(_Stream(width, height)));

  [Test]
  [Category("Unit")]
  public void AnotherBitDepthRefuses() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("RT21"),
      Width = 8,
      Height = 4,
      BitsPerPixel = 16,
    };

    Assert.Throws<NotSupportedException>(() => Indeo2VideoEncoder.Create(stream));
  }

  [Test]
  [Category("Unit")]
  public void NeutralNativePlanesRoundTripExactlyThroughAnAvi() {
    const int width = 8;
    const int height = 4;
    var encoder = Indeo2VideoEncoder.Create(_Stream(width, height));

    Assert.That(encoder.TryEncode(_NeutralYuv(width, height), 12, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(12));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(12));
      Assert.That(packet.Data.Length, Is.GreaterThan(48));
      Assert.That(packet.Data.Span[18], Is.Not.Zero, "the packet is an intra frame");
      Assert.That(packet.Data.Span[0x22] >> 4, Is.Zero, "only the two documented table-selector pairs are set");
    });

    var stream = encoder.DescribeStream();
    var avi = VideoIO.Mux<AviWriter>([stream], [packet]);
    var container = AviContainer.FromBytes(avi);
    var readStream = AviContainer.Streams(container).Single();
    var readPacket = AviContainer.ReadPackets(container).Single(p => p.StreamIndex == readStream.Index);
    var decoder = (Indeo2VideoDecoder)VideoFormatRegistry.CreateDecoder(readStream);

    Assert.That(decoder.TryDecode(readPacket, out var decoded), Is.True);
    Assert.Multiple(() => {
      Assert.That(readStream.Codec, Is.EqualTo(CodecTag.FromCharacters("RT21")));
      Assert.That(decoded.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
      Assert.That(decoder.Planes.Luma, Is.All.EqualTo(128));
      Assert.That(decoder.Planes.Cb, Is.All.EqualTo(128));
      Assert.That(decoder.Planes.Cr, Is.All.EqualTo(128));
    });
  }

  [Test]
  [Category("Unit")]
  public void TruncatedSourceRefuses() {
    var encoder = Indeo2VideoEncoder.Create(_Stream(8, 4));
    var image = new RawImage {
      Width = 8,
      Height = 4,
      Format = PixelFormat.Rgba32,
      PixelData = [0],
    };

    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(image, 0, out _));
  }

  [Test]
  [Category("Unit")]
  public void MidStreamGeometryChangeRefuses() {
    var encoder = Indeo2VideoEncoder.Create(_Stream(8, 4));
    var image = _NeutralYuv(16, 4);

    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(image, 0, out _));
  }

  [Test]
  [Category("Unit")]
  public void HuffmanWriterIsTheInverseOfTheReaderForEverySymbol() {
    var writer = new Indeo2BitWriter();
    foreach (var entry in Indeo2Tables.Codes)
      writer.WriteSymbol(entry.Symbol);

    var reader = new Indeo2BitReader(writer.ToArray());
    foreach (var entry in Indeo2Tables.Codes)
      Assert.That(reader.ReadSymbol(), Is.EqualTo(entry.Symbol));
  }

  [Test]
  [Category("Unit")]
  public void AnInterRunCannotCrossItsScanline() {
    var bits = new Indeo2BitWriter();
    bits.WriteSymbol(0x84); // run of five pairs / ten samples on an eight-sample line
    var body = bits.ToArray();
    var frame = new byte[48 + body.Length];
    body.CopyTo(frame, 48);

    var decoder = new Indeo2FrameDecoder(8, 4);
    var exception = Assert.Throws<InvalidDataException>(() => decoder.Decode(frame));
    Assert.That(exception!.Message, Does.Contain("overruns the 8-sample line"));
  }

  [Test]
  [Category("Unit")]
  public void AGroupOpensWithAnIntraFrameAndContinuesWithInterFrames() {
    var encoder = Indeo2VideoEncoder.Create(_Stream(64, 48));
    var keyFrames = new List<bool>();
    var intraFlags = new List<byte>();

    for (var index = 0; index < 3; ++index) {
      Assert.That(encoder.TryEncode(_MovingSquare(64, 48, index), index, out var packet), Is.True);
      keyFrames.Add(packet.IsKeyFrame);

      // The intra flag sits at offset 18 of the 48-byte frame header; any non-zero value means intra.
      intraFlags.Add(packet.Data.Span[18]);
    }

    Assert.Multiple(() => {
      Assert.That(keyFrames, Is.EqualTo(new[] { true, false, false }));
      Assert.That(intraFlags[0], Is.Not.Zero, "the first frame of a group states intra");
      Assert.That(intraFlags[1], Is.Zero);
      Assert.That(intraFlags[2], Is.Zero);
    });
  }

  [Test]
  [Category("RoundTrip")]
  public void AMovingSquareSurvivesInterCodingWithoutDrifting() {
    // A whole group: one intra frame and eleven inter frames, so the last frame is as far from an
    // intra frame as this encoder ever places one. An encoder predicting from its source rather than
    // from its own reconstruction drifts further with every inter frame, and Indeo 2's deltas are too
    // small to pull that back -- which comparing the LAST frame catches and comparing the first cannot.
    const int width = 64;
    const int height = 48;
    var stream = _Stream(width, height);
    var encoder = Indeo2VideoEncoder.Create(stream);
    var decoder = Indeo2VideoDecoder.Create(encoder.DescribeStream());

    var worst = 0d;
    for (var index = 0; index < 12; ++index) {
      var source = _MovingSquare(width, height, index);
      Assert.That(encoder.TryEncode(source, index, out var packet), Is.True);
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      worst = Math.Max(worst, _MeanAbsoluteError(source, decoded));
    }

    // Indeo 2 quantises every sample pair against a 128-entry delta table, so the bar is that the
    // picture is recognisably the one that went in rather than that it is identical.
    Assert.That(worst, Is.LessThan(24d), "an inter frame drifted away from its source across the group");
  }

  [Test]
  [Category("RoundTrip")]
  public void AStillSceneCollapsesToSkippedPairs() {
    // What an inter frame is for. The first one still corrects the intra frame's own quantisation
    // error, but once that correction is in the reference every pair is skipped and the frame is
    // little more than its header. An encoder predicting from the source would never converge.
    const int width = 64;
    const int height = 48;
    var encoder = Indeo2VideoEncoder.Create(_Stream(width, height));
    var picture = _MovingSquare(width, height, 0);
    var sizes = new List<int>();

    for (var index = 0; index < 6; ++index)
      if (encoder.TryEncode(picture, index, out var packet))
        sizes.Add(packet.Data.Length);

    // The floor is structural rather than lazy: Indeo 2 has no "nothing changed" frame, and a run
    // states at most sixteen pairs, so even a frame that skips everything still spends two run codes
    // a luminance line plus the forty-eight byte header. What matters is that the size settles
    // instead of tracking the intra frame, and that it settles low.
    Assert.That(sizes[^1], Is.LessThan(sizes[0] / 2d),
      $"a settled inter frame is {sizes[^1]} bytes against {sizes[0]} for the intra one; "
      + $"the run was {string.Join(", ", sizes)}");
    Assert.That(sizes[^1], Is.EqualTo(sizes[^2]),
      "a still scene should have stopped changing size before the last frame");
  }

  [Test]
  [Category("Oracle")]
  public void FFmpegDecodesEveryInterFrameAndNotOnlyTheIntraOne() {
    // The registry's oracle asks FFmpeg for the first frame only, which is the intra one -- already
    // correct before inter frames existed. A delta scaled with the wrong rounding, or a run that means
    // "neutral" where it should mean "unchanged", would sail past that and fail on frame two.
    FFmpegOracle.RequireAvailable();

    const int width = 64;
    const int height = 48;
    const int frames = 24; // Two whole groups, so a group boundary is crossed as well.

    var encoder = Indeo2VideoEncoder.Create(_Stream(width, height));
    var packets = new List<CodedPacket>();
    var sources = new List<RawImage>();

    for (var index = 0; index < frames; ++index) {
      var picture = _MovingSquare(width, height, index);
      sources.Add(picture);
      if (encoder.TryEncode(picture, index, out var packet))
        packets.Add(packet);
    }

    var directory = Directory.CreateTempSubdirectory("indeo2-oracle");
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
        "ffmpeg read a different number of frames than were written");

      for (var index = 0; index < frames; ++index) {
        var total = 0L;
        for (var offset = 0; offset < frameBytes; ++offset)
          total += Math.Abs(sources[index].PixelData[offset] - decoded[index * frameBytes + offset]);

        Assert.That(total / (double)frameBytes, Is.LessThan(24d),
          $"ffmpeg's frame {index} is not the frame that was encoded");
      }
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }

  private static double _MeanAbsoluteError(RawImage expected, RawImage actual) {
    var left = expected.PixelData;
    var right = actual.PixelData;
    var total = 0L;
    var count = Math.Min(left.Length, right.Length);
    for (var index = 0; index < count; ++index)
      total += Math.Abs(left[index] - right[index]);

    return total / (double)count;
  }

  /// <summary>A bright square crossing a fixed background.</summary>
  private static RawImage _MovingSquare(int width, int height, int phase) {
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      var background = (byte)(60 + ((x / 8 + y / 8) & 1) * 30);
      data[at] = background;
      data[at + 1] = background;
      data[at + 2] = background;
    }

    var squareX = 4 + phase;
    var squareY = 4 + phase;
    for (var y = squareY; y < Math.Min(squareY + 16, height); ++y)
    for (var x = squareX; x < Math.Min(squareX + 16, width); ++x) {
      var at = (y * width + x) * 3;
      data[at] = 220;
      data[at + 1] = 200;
      data[at + 2] = 90;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("RT21"),
    Width = width,
    Height = height,
  };

  private static RawImage _NeutralYuv(int width, int height) {
    var samples = width * height * 3;
    var data = new byte[samples];
    Array.Fill(data, (byte)128);
    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Yuv444P8,
      PixelData = data,
    };
  }
}
