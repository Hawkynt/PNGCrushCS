using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;
using NUnit.Framework;

namespace FileFormat.Codecs.CineForm.Tests;

[TestFixture]
public sealed class CineFormBayerTests {
  private static readonly CodecTag _Cfhd = CodecTag.FromCharacters("CFHD");

  [Test]
  [Category("Unit")]
  public void WriterUsesHalfResolutionDecorrelatedBayerHeader() {
    const int WIDTH = 64;
    const int HEIGHT = 48;
    var encoder = CineFormVideoEncoder.Create(_Stream(WIDTH, HEIGHT), CineFormEncodingFormat.BayerRggb12);
    var frame = _FlatRggb12(WIDTH, HEIGHT, r: 1200, g1: 1800, g2: 1600, b: 2400);

    Assert.That(encoder.TryEncode(frame, 7, out var packet), Is.True);
    var decoded = CineFormVideoDecoder.Create(encoder.DescribeStream()).DecodeChannels(packet.Data);

    Assert.Multiple(() => {
      Assert.That(_HeaderValue(packet.Data.Span, CineFormTags.ChannelCount), Is.EqualTo(4));
      Assert.That(_HeaderValue(packet.Data.Span, CineFormTags.EncodedFormat), Is.EqualTo((ushort)CineFormEncodedFormat.Bayer));
      Assert.That(_HeaderValue(packet.Data.Span, CineFormTags.InputFormat), Is.EqualTo(104));
      Assert.That(_HeaderValue(packet.Data.Span, CineFormTags.Precision), Is.EqualTo(12));
      Assert.That(_HeaderValue(packet.Data.Span, CineFormTags.PrescaleTable), Is.EqualTo(0x2800));
      Assert.That(_HeaderValue(packet.Data.Span, CineFormTags.ImageWidth), Is.EqualTo(WIDTH / 2));
      Assert.That(_HeaderValue(packet.Data.Span, CineFormTags.ImageHeight), Is.EqualTo(HEIGHT / 2));
      Assert.That(encoder.DescribeStream().BitsPerPixel, Is.EqualTo(12));
      Assert.That(decoded.IsBayer, Is.True);
      Assert.That(decoded.ImageWidth, Is.EqualTo(WIDTH));
      Assert.That(decoded.ImageHeight, Is.EqualTo(HEIGHT));
      Assert.That(decoded.Channels, Has.Length.EqualTo(4));
      Assert.That(decoded.Channels[0].Width, Is.EqualTo(WIDTH / 2));
      Assert.That(decoded.Channels[0].Height, Is.EqualTo(HEIGHT / 2));
    });
  }

  [Test]
  [Category("Unit")]
  public void PublicDecoderReturnsTheOriginalRawRggbMosaic() {
    const int WIDTH = 64;
    const int HEIGHT = 48;
    var encoder = CineFormVideoEncoder.Create(_Stream(WIDTH, HEIGHT), CineFormEncodingFormat.BayerRggb12);
    var source = _FlatRggb12(WIDTH, HEIGHT, r: 1200, g1: 1800, g2: 1600, b: 2400);

    Assert.That(encoder.TryEncode(source, 0, out var packet), Is.True);
    var decoder = CineFormVideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var actual), Is.True);

    Assert.Multiple(() => {
      Assert.That(actual.Format, Is.EqualTo(PixelFormat.Cfa16));
      Assert.That(actual.Width, Is.EqualTo(WIDTH));
      Assert.That(actual.Height, Is.EqualTo(HEIGHT));
      Assert.That(actual.CfaInfo, Is.EqualTo(new RawCfaInfo(RawCfaPattern.Rggb, 12)));
      Assert.That(actual.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void DisplayHeightIsRestoredFromHalfResolutionBayerGeometry() {
    const int WIDTH = 64;
    const int HEIGHT = 50;
    var encoder = CineFormVideoEncoder.Create(_Stream(WIDTH, HEIGHT), CineFormEncodingFormat.BayerRggb12);
    var source = _FlatRggb12(WIDTH, HEIGHT, 1000, 1500, 1700, 2200);

    Assert.That(encoder.TryEncode(source, 0, out var packet), Is.True);
    var decoded = CineFormVideoDecoder.Create(encoder.DescribeStream()).DecodeChannels(packet.Data);

    Assert.Multiple(() => {
      Assert.That(_HeaderValue(packet.Data.Span, CineFormTags.ImageHeight), Is.EqualTo(32));
      Assert.That(_HeaderValue(packet.Data.Span, CineFormTags.DisplayHeight), Is.EqualTo(25));
      Assert.That(decoded.ImageHeight, Is.EqualTo(HEIGHT));
      Assert.That(decoded.Channels[0].Height, Is.EqualTo(32), "component plane retains coded padding");
    });
  }

  [Test]
  [Category("Unit")]
  public void WriterRefusesBayerPhaseWithoutBfmtMetadataAndNonTwelveBitInput() {
    const int WIDTH = 64;
    const int HEIGHT = 48;
    var encoder = CineFormVideoEncoder.Create(_Stream(WIDTH, HEIGHT), CineFormEncodingFormat.BayerRggb12);

    var wrongPhase = _FlatCfa(WIDTH, HEIGHT, RawCfaPattern.Bggr, 12, 1000);
    var wrongDepth = _FlatCfa(WIDTH, HEIGHT, RawCfaPattern.Rggb, 10, 500);

    Assert.Multiple(() => {
      var phase = Assert.Throws<NotSupportedException>(() => encoder.TryEncode(wrongPhase, 0, out _));
      Assert.That(phase!.Message, Does.Contain("BFMT"));
      var depth = Assert.Throws<NotSupportedException>(() => encoder.TryEncode(wrongDepth, 0, out _));
      Assert.That(depth!.Message, Does.Contain("12 bits"));
    });
  }

  [Test]
  [Category("Unit")]
  public void WriterRefusesOddBayerHeight() {
    var failure = Assert.Throws<NotSupportedException>(() =>
      CineFormVideoEncoder.Create(_Stream(64, 49), CineFormEncodingFormat.BayerRggb12));
    Assert.That(failure!.Message, Does.Contain("even"));
  }

  [Test]
  [Category("Conformance")]
  public void FfmpegDecodesWriterOutputAsRggb16WhenAvailable() {
    FFmpegOracle.RequireAvailable();

    const int WIDTH = 64;
    const int HEIGHT = 48;
    const ushort R = 1200;
    const ushort G1 = 1800;
    const ushort G2 = 1600;
    const ushort B = 2400;

    var encoder = CineFormVideoEncoder.Create(_Stream(WIDTH, HEIGHT), CineFormEncodingFormat.BayerRggb12);
    Assert.That(encoder.TryEncode(_FlatRggb12(WIDTH, HEIGHT, R, G1, G2, B), 0, out var packet), Is.True);

    var avi = VideoIO.Mux<AviWriter>([encoder.DescribeStream()], [packet]);
    var inputPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");
    var outputPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".raw");

    try {
      File.WriteAllBytes(inputPath, avi);
      var startInfo = new ProcessStartInfo(FFmpegOracle.ExecutablePath!) {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
      };
      foreach (var argument in new[] {
        "-hide_banner", "-loglevel", "error", "-y", "-i", inputPath,
        "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "bayer_rggb16le", outputPath,
      })
        startInfo.ArgumentList.Add(argument);

      using var process = Process.Start(startInfo);
      Assert.That(process, Is.Not.Null, "ffmpeg would not start");
      var stdout = process!.StandardOutput.ReadToEndAsync();
      var stderr = process.StandardError.ReadToEndAsync();
      if (!process.WaitForExit(60_000)) {
        try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        Assert.Fail("ffmpeg timed out while decoding a CineForm Bayer oracle frame");
      }

      var diagnostics = string.Concat(stdout.Result, stderr.Result).Trim();
      Assert.That(process.ExitCode, Is.Zero, diagnostics);
      Assert.That(diagnostics, Is.Empty, diagnostics);

      var raw = File.ReadAllBytes(outputPath);
      Assert.That(raw, Has.Length.EqualTo(WIDTH * HEIGHT * 2));
      Assert.Multiple(() => {
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(0)), Is.EqualTo(R << 4), "R");
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(2)), Is.EqualTo(G1 << 4), "G1");
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(WIDTH * 2)), Is.EqualTo(G2 << 4), "G2");
        Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(WIDTH * 2 + 2)), Is.EqualTo(B << 4), "B");
      });
    } finally {
      try { File.Delete(inputPath); } catch { /* best effort */ }
      try { File.Delete(outputPath); } catch { /* best effort */ }
    }
  }

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 11,
    Kind = MediaStreamKind.Video,
    Codec = _Cfhd,
    Handler = _Cfhd,
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _FlatRggb12(int width, int height, ushort r, ushort g1, ushort g2, ushort b) {
    if ((width & 1) != 0 || (height & 1) != 0)
      throw new ArgumentOutOfRangeException(nameof(width));

    var data = new byte[width * height * 2];
    for (var y = 0; y < height; y += 2)
    for (var x = 0; x < width; x += 2) {
      _Write(data, width, x, y, r);
      _Write(data, width, x + 1, y, g1);
      _Write(data, width, x, y + 1, g2);
      _Write(data, width, x + 1, y + 1, b);
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Cfa16,
      PixelData = data,
      CfaInfo = new(RawCfaPattern.Rggb, 12),
    };
  }

  private static RawImage _FlatCfa(int width, int height, RawCfaPattern pattern, int bitDepth, ushort value) {
    var data = new byte[width * height * 2];
    for (var i = 0; i < width * height; ++i)
      BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(i * 2), value);

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Cfa16,
      PixelData = data,
      CfaInfo = new(pattern, bitDepth),
    };
  }

  private static void _Write(Span<byte> data, int width, int x, int y, ushort value) {
    if (value > 4095)
      throw new ArgumentOutOfRangeException(nameof(value));
    BinaryPrimitives.WriteUInt16LittleEndian(data[((y * width + x) * 2)..], value);
  }

  private static ushort _HeaderValue(ReadOnlySpan<byte> packet, int wantedTag) {
    var position = 0;
    while (position + 4 <= packet.Length) {
      var tag = (short)BinaryPrimitives.ReadUInt16BigEndian(packet[position..]);
      var value = BinaryPrimitives.ReadUInt16BigEndian(packet[(position + 2)..]);
      position += 4;
      if (tag == wantedTag || tag == -wantedTag)
        return value;
      if (tag == CineFormTags.Index) {
        var bytes = checked(value * 4);
        if (position > packet.Length - bytes)
          throw new InvalidDataException("CineForm test packet ended inside its channel index.");
        position += bytes;
      }
      if (tag == CineFormTags.LowpassPrecision)
        break;
    }

    throw new InvalidDataException($"CineForm test packet did not contain header tag {wantedTag}.");
  }
}
