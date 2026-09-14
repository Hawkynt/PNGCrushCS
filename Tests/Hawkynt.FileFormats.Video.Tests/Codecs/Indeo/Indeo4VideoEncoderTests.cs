using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Indeo.Tests;

[TestFixture]
public sealed class Indeo4VideoEncoderTests {

  private static MediaStreamInfo _Stream(int width, int height, int index = 0) => new() {
    Index = index,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("IV41"),
    Handler = CodecTag.FromCharacters("IV41"),
    Width = width,
    Height = height,
    TimeBase = new(1, 25),
    FrameRate = new(25, 1),
  };

  [Test]
  [Category("Unit")]
  public void TheRegistryBuildsTheIndeo4Encoder() {
    var stream = _Stream(64, 48);
    Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<Indeo4VideoEncoder>());
  }

  [Test]
  [Category("Unit")]
  public void TheStreamDescriptionNamesARealIv41VfwStream() {
    var encoder = Indeo4VideoEncoder.Create(_Stream(73, 51, index: 3));
    var stream = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("IV41")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("IV41")));
      Assert.That(stream.CodecId, Is.EqualTo("V_MS/VFW/FOURCC"));
      Assert.That(stream.Width, Is.EqualTo(73));
      Assert.That(stream.Height, Is.EqualTo(51));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(24));
      Assert.That(stream.CodecPrivateData.Length, Is.EqualTo(40));
      Assert.That(stream.Index, Is.EqualTo(3));
    });
  }

  [TestCase(1, 1)]
  [TestCase(17, 9)]
  [TestCase(64, 48)]
  [TestCase(257, 259)]
  [Category("Unit")]
  public void WhatTheEncoderWritesTheIndeoDecoderReads(int width, int height) {
    var frame = _Picture(width, height);
    var encoder = Indeo4VideoEncoder.Create(_Stream(width, height, index: 3));
    var packets = _EncodeAll(encoder, [(frame, 17L)]);

    Assert.That(packets, Has.Count.EqualTo(1));
    var packet = packets[0];
    var decoded = new Indeo4Decoder().Decode(packet.Data);

    Assert.That(decoded, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(decoded!.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
      Assert.That(packet.StreamIndex, Is.EqualTo(3));
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(17));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(17));
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(decoded.Luma, Is.EqualTo(_ExpectedLuma(frame)));
    });

    var (expectedBlue, expectedRed) = _ExpectedChroma(frame);
    Assert.Multiple(() => {
      Assert.That(decoded!.ChromaBlue.Length, Is.EqualTo(expectedBlue.Length));
      Assert.That(decoded.ChromaRed.Length, Is.EqualTo(expectedRed.Length));
      Assert.That(_MaximumDifference(decoded.ChromaBlue, expectedBlue), Is.LessThanOrEqualTo(1));
      Assert.That(_MaximumDifference(decoded.ChromaRed, expectedRed), Is.LessThanOrEqualTo(1));
    });
  }

  [Test]
  [Category("RoundTrip")]
  public void AWholeGroupDecodesInDisplayOrderAsIbp() {
    const int width = 64;
    const int height = 48;
    var sources = Enumerable.Range(0, 6).Select(i => _MovingGray(width, height, i)).ToArray();
    var encoder = Indeo4VideoEncoder.Create(_Stream(width, height));
    var input = sources.Select((frame, index) => (frame, (long?)index));
    var packets = _EncodeAll(encoder, input);

    Assert.Multiple(() => {
      Assert.That(packets, Has.Count.EqualTo(sources.Length));
      Assert.That(packets.Select(_FrameType), Is.EqualTo(new[] {
        Indeo4Decoder.FrameTypeIntra,
        Indeo4Decoder.FrameTypeBidirectional,
        Indeo4Decoder.FrameTypeNullLast,
        Indeo4Decoder.FrameTypeIntra,
        Indeo4Decoder.FrameTypeBidirectional,
        Indeo4Decoder.FrameTypeNullLast,
      }));
      Assert.That(packets.Select(p => p.IsKeyFrame), Is.EqualTo(new[] { true, false, false, true, false, false }));
      Assert.That(packets.Select(p => p.PresentationTimestamp), Is.EqualTo(Enumerable.Range(0, 6).Select(i => (long?)i)));
    });

    var decoder = new Indeo4Decoder();
    for (var index = 0; index < packets.Count; ++index) {
      var decoded = decoder.Decode(packets[index].Data);
      Assert.That(decoded, Is.Not.Null, $"packet {index} produced no display picture");
      Assert.That(decoded!.Luma, Is.EqualTo(_ExpectedLuma(sources[index])), $"luma differs in display picture {index}");

      var (blue, red) = _ExpectedChroma(sources[index]);
      Assert.Multiple(() => {
        Assert.That(_MaximumDifference(decoded.ChromaBlue, blue), Is.LessThanOrEqualTo(2), $"Cb differs in picture {index}");
        Assert.That(_MaximumDifference(decoded.ChromaRed, red), Is.LessThanOrEqualTo(2), $"Cr differs in picture {index}");
      });
    }
  }

  [Test]
  [Category("Unit")]
  public void APerfectMidpointUsesBothReferences() {
    const int width = 32;
    const int height = 32;
    var encoder = Indeo4VideoEncoder.Create(_Stream(width, height));
    var packets = _EncodeAll(encoder, [
      (_SolidGray(width, height, 0), 0L),
      (_SolidGray(width, height, 127), 1L),
      (_SolidGray(width, height, 255), 2L),
    ]);

    Assert.That(packets, Has.Count.EqualTo(3));
    Assert.That(_FrameType(packets[1]), Is.EqualTo(Indeo4Decoder.FrameTypeBidirectional));
    Assert.That(_FirstLumaMacroblockType(packets[1]), Is.EqualTo(3),
      "a midpoint macroblock should use the average of its forward and backward references");
  }

  [Test]
  [Category("RoundTrip")]
  public void AShortTailFlushesAsIThenP() {
    const int width = 33;
    const int height = 19;
    var first = _Picture(width, height, seed: 1);
    var second = _Picture(width, height, seed: 2);
    var encoder = Indeo4VideoEncoder.Create(_Stream(width, height));

    Assert.That(encoder.TryEncode(first, 5, out _), Is.False);
    Assert.That(encoder.TryEncode(second, 6, out _), Is.False);
    var packets = encoder.Flush().ToArray();

    Assert.Multiple(() => {
      Assert.That(packets, Has.Length.EqualTo(2));
      Assert.That(_FrameType(packets[0]), Is.EqualTo(Indeo4Decoder.FrameTypeIntra));
      Assert.That(_FrameType(packets[1]), Is.EqualTo(Indeo4Decoder.FrameTypeInter));
      Assert.That(packets.Select(p => p.IsKeyFrame), Is.EqualTo(new[] { true, false }));
      Assert.That(packets.Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { 5, 6 }));
    });

    var decoder = new Indeo4Decoder();
    Assert.That(decoder.Decode(packets[0].Data)!.Luma, Is.EqualTo(_ExpectedLuma(first)));
    Assert.That(decoder.Decode(packets[1].Data)!.Luma, Is.EqualTo(_ExpectedLuma(second)));
  }

  [Test]
  [Category("Oracle")]
  public void FFmpegDecodesPackedBidirectionalGroups() {
    FFmpegOracle.RequireAvailable();

    const int width = 64;
    const int height = 48;
    const int frameCount = 9;
    var sources = Enumerable.Range(0, frameCount).Select(i => _MovingGray(width, height, i)).ToArray();
    var encoder = Indeo4VideoEncoder.Create(_Stream(width, height));
    var packets = _EncodeAll(encoder, sources.Select((frame, index) => (frame, (long?)index)));
    var directory = Directory.CreateTempSubdirectory("indeo4-oracle");

    try {
      var path = Path.Combine(directory.FullName, "clip.avi");
      File.WriteAllBytes(path, VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets));

      var raw = Path.Combine(directory.FullName, "decoded.rgb");
      var startInfo = new ProcessStartInfo(FFmpegOracle.ExecutablePath!) {
        RedirectStandardError = true,
        UseShellExecute = false,
      };
      foreach (var argument in new[] {
        "-hide_banner", "-loglevel", "error", "-i", path,
        "-f", "rawvideo", "-pix_fmt", "rgb24", raw,
      })
        startInfo.ArgumentList.Add(argument);

      using var process = Process.Start(startInfo)!;
      var diagnostics = process.StandardError.ReadToEnd();
      process.WaitForExit(60_000);

      Assert.That(process.ExitCode, Is.Zero, $"ffmpeg refused the IV41 stream: {diagnostics}");
      var decoded = File.ReadAllBytes(raw);
      var frameBytes = width * height * 3;
      Assert.That(decoded.Length / frameBytes, Is.EqualTo(frameCount),
        "ffmpeg produced a different number of display frames than were encoded");

      for (var index = 0; index < frameCount; ++index) {
        var expected = sources[index].ToRgb24();
        long total = 0;
        for (var offset = 0; offset < frameBytes; ++offset)
          total += Math.Abs(expected[offset] - decoded[index * frameBytes + offset]);

        Assert.That(total / (double)frameBytes, Is.LessThan(8d),
          $"ffmpeg's display frame {index} is not the grayscale picture that was encoded");
      }
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }

  [TestCase(0, 48)]
  [TestCase(64, 0)]
  [TestCase(65536, 1)]
  [Category("Unit")]
  public void ASizeTheIv4HeaderCannotStateIsRefused(int width, int height)
    => Assert.Throws<NotSupportedException>(() => Indeo4VideoEncoder.Create(_Stream(width, height)));

  [Test]
  [Category("Unit")]
  public void AFrameWhoseGeometryDoesNotMatchTheStreamIsRefused() {
    var encoder = Indeo4VideoEncoder.Create(_Stream(16, 16));
    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Picture(17, 16), null, out _));
    Assert.That(failure!.Message, Does.Contain("fixed at 16x16"));
  }

  [Test]
  [Category("Unit")]
  public void TruncatedSourcePixelsAreRefused() {
    var encoder = Indeo4VideoEncoder.Create(_Stream(4, 4));
    var frame = new RawImage {
      Width = 4,
      Height = 4,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[4 * 4 * 3 - 1],
    };

    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(frame, null, out _));
  }

  private static List<CodedPacket> _EncodeAll(
    Indeo4VideoEncoder encoder,
    IEnumerable<(RawImage Frame, long? Timestamp)> frames) {
    var result = new List<CodedPacket>();
    foreach (var (frame, timestamp) in frames)
      if (encoder.TryEncode(frame, timestamp, out var packet))
        result.Add(packet);

    result.AddRange(encoder.Flush());
    return result;
  }

  private static int _FrameType(CodedPacket packet) {
    var reader = new IviBitReader(packet.Data);
    Assert.That(reader.Read(18), Is.EqualTo(0x3FFF8));
    return (int)reader.Read(3);
  }

  private static int _FirstLumaMacroblockType(CodedPacket packet) {
    var reader = new IviBitReader(packet.Data);
    Assert.That(reader.Read(18), Is.EqualTo(0x3FFF8));
    Assert.That(reader.Read(3), Is.EqualTo(Indeo4Decoder.FrameTypeBidirectional));
    reader.Skip(2);
    Assert.That(reader.ReadFlag(), Is.False);
    Assert.That(reader.ReadFlag(), Is.False);
    Assert.That(reader.Read(3), Is.EqualTo(7));
    reader.Skip(32);
    Assert.That(reader.ReadFlag(), Is.True);
    reader.Skip(8);
    Assert.That(reader.Read(2), Is.Zero);
    Assert.That(reader.Read(2), Is.EqualTo(3));
    Assert.That(reader.Read(2), Is.EqualTo(3));
    Assert.That(reader.ReadFlag(), Is.False);
    Assert.That(reader.ReadFlag(), Is.False);
    _SkipFixedCodebook(reader);
    _SkipFixedCodebook(reader);
    Assert.That(reader.ReadFlag(), Is.False);
    reader.Skip(1 + 1 + 5);
    Assert.That(reader.ReadFlag(), Is.False);
    Assert.That(reader.ReadFlag(), Is.False);
    Assert.That(reader.ReadFlag(), Is.False);
    reader.Skip(1);
    reader.Align();

    Assert.That(reader.Read(2), Is.Zero);
    Assert.That(reader.Read(4), Is.Zero);
    Assert.That(reader.ReadFlag(), Is.False);
    Assert.That(reader.ReadFlag(), Is.False);
    Assert.That(reader.Read(2), Is.Zero);
    Assert.That(reader.ReadFlag(), Is.False);
    Assert.That(reader.Read(2), Is.Zero);
    Assert.That(reader.ReadFlag(), Is.False);
    Assert.That(reader.ReadFlag(), Is.False);
    Assert.That(reader.Read(5), Is.Zero);
    Assert.That(reader.ReadFlag(), Is.False);
    Assert.That(reader.Read(5), Is.EqualTo(3));
    Assert.That(reader.Read(4), Is.Zero);
    Assert.That(reader.Read(5), Is.Zero);
    Assert.That(reader.ReadFlag(), Is.False);
    Assert.That(reader.ReadFlag(), Is.False);
    Assert.That(reader.ReadFlag(), Is.False);
    reader.Align();

    Assert.That(reader.ReadFlag(), Is.False);
    Assert.That(reader.ReadFlag(), Is.True);
    var tileLength = (int)reader.Read(8);
    if (tileLength == 255)
      reader.Skip(24);
    reader.Align();

    Assert.That(reader.ReadFlag(), Is.False, "type 3 cannot use the forward-repeat shorthand");
    return (int)reader.Read(2);
  }

  private static void _SkipFixedCodebook(IviBitReader reader) {
    Assert.That(reader.ReadFlag(), Is.True);
    Assert.That(reader.Read(3), Is.EqualTo(7));
    Assert.That(reader.Read(4), Is.EqualTo(1));
    Assert.That(reader.Read(4), Is.EqualTo(6));
  }

  private static RawImage _Picture(int width, int height, int seed = 0) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        pixels[at] = (byte)((x * 37 + y * 11 + seed * 53) & 0xFF);
        pixels[at + 1] = (byte)((x * 7 + y * 29 + seed * 31) & 0xFF);
        pixels[at + 2] = (byte)((x * 19 + y * 3 + seed * 17) & 0xFF);
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  private static RawImage _SolidGray(int width, int height, byte value) {
    var pixels = new byte[width * height * 3];
    pixels.AsSpan().Fill(value);
    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  private static RawImage _MovingGray(int width, int height, int phase) {
    var pixels = new byte[width * height * 3];
    var left = phase * 5 % Math.Max(1, width - 12);
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var value = (byte)(x >= left && x < left + 12 && y >= 12 && y < Math.Min(height, 28)
          ? 210
          : 56 + ((x / 8 + y / 8) & 1) * 24);
        var at = (y * width + x) * 3;
        pixels[at] = pixels[at + 1] = pixels[at + 2] = value;
      }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  private static byte[] _ExpectedLuma(RawImage frame) {
    var rgb = frame.ToRgb24();
    var result = new byte[frame.Width * frame.Height];
    for (var i = 0; i < result.Length; ++i) {
      var at = i * 3;
      var r = rgb[at];
      var g = rgb[at + 1];
      var b = rgb[at + 2];
      result[i] = _Clamp(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16);
    }

    return result;
  }

  private static (byte[] Blue, byte[] Red) _ExpectedChroma(RawImage frame) {
    var rgb = frame.ToRgb24();
    var width = (frame.Width + 3) >> 2;
    var height = (frame.Height + 3) >> 2;
    var blue = new byte[width * height];
    var red = new byte[blue.Length];
    var blueTotals = new int[blue.Length];
    var redTotals = new int[blue.Length];
    var counts = new int[blue.Length];

    for (var y = 0; y < frame.Height; ++y)
      for (var x = 0; x < frame.Width; ++x) {
        var source = (y * frame.Width + x) * 3;
        var at = (y >> 2) * width + (x >> 2);
        var r = rgb[source];
        var g = rgb[source + 1];
        var b = rgb[source + 2];
        blueTotals[at] += ((-38 * r - 74 * g + 112 * b + 128) >> 8) + 128;
        redTotals[at] += ((112 * r - 94 * g - 18 * b + 128) >> 8) + 128;
        ++counts[at];
      }

    for (var i = 0; i < blue.Length; ++i) {
      blue[i] = _Clamp((blueTotals[i] + counts[i] / 2) / counts[i]);
      red[i] = _Clamp((redTotals[i] + counts[i] / 2) / counts[i]);
    }

    return (blue, red);
  }

  private static int _MaximumDifference(byte[] actual, byte[] expected)
    => actual.Zip(expected, static (a, e) => Math.Abs(a - e)).Max();

  private static byte _Clamp(int value) => (byte)(value < 0 ? 0 : value > 255 ? 255 : value);
}
