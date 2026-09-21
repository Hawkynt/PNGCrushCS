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

    var raw = _DecodeRggb16WithFfmpeg(encoder.DescribeStream(), packet, WIDTH, HEIGHT);
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(0)), Is.EqualTo(R << 4), "R");
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(2)), Is.EqualTo(G1 << 4), "G1");
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(WIDTH * 2)), Is.EqualTo(G2 << 4), "G2");
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(WIDTH * 2 + 2)), Is.EqualTo(B << 4), "B");
    });
  }

  /// <summary>
  /// The flat oracle above proves the four decorrelated components are centred and ordered the way
  /// FFmpeg's <c>process_bayer</c> inverts them, but it proves it for one 2x2 cell of a picture whose
  /// every cell is identical. A transposed component plane, a half-resolution index computed from the
  /// wrong dimension, or padding copied from the wrong row all survive that: constant data is constant
  /// however you walk it. This drives a mosaic that varies in both axes and per sensor colour, then
  /// holds <i>every</i> site against what FFmpeg reconstructs, so geometry has to be right everywhere
  /// rather than at the origin.
  /// </summary>
  [Test]
  [Category("Conformance")]
  public void FfmpegReconstructsVaryingBayerGeometryEverywhere() {
    FFmpegOracle.RequireAvailable();

    const int WIDTH = 96;
    const int HEIGHT = 64;

    var encoder = CineFormVideoEncoder.Create(_Stream(WIDTH, HEIGHT), CineFormEncodingFormat.BayerRggb12);
    var source = _GradientRggb12(WIDTH, HEIGHT);
    Assert.That(encoder.TryEncode(source, 0, out var packet), Is.True);

    var raw = _DecodeRggb16WithFfmpeg(encoder.DescribeStream(), packet, WIDTH, HEIGHT);

    // Measured, not assumed: on this picture every one of the 6,144 sensor sites comes back from
    // ffmpeg bit-exact, so the bound is zero rather than a fudge factor. CineForm's wavelet is lossy in
    // general -- its highpasses are quantised -- but these four components are planar ramps whose
    // highpass coefficients survive the twelve-bit prescale intact. Both halves are integer arithmetic
    // with no platform-dependent rounding, so if this ever stops holding exactly, the cause is a real
    // change in one of the two codecs and is worth looking at rather than papering over.
    const int TOLERANCE = 0;

    var worst = 0;
    var worstAt = (X: -1, Y: -1);
    for (var y = 0; y < HEIGHT; ++y)
    for (var x = 0; x < WIDTH; ++x) {
      var offset = (y * WIDTH + x) * 2;
      var expected = BinaryPrimitives.ReadUInt16LittleEndian(source.PixelData.AsSpan(offset)) << 4;
      var actual = BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(offset));
      var delta = Math.Abs(expected - actual);
      if (delta <= worst)
        continue;

      worst = delta;
      worstAt = (x, y);
    }

    Assert.That(
      worst,
      Is.LessThanOrEqualTo(TOLERANCE),
      $"FFmpeg's reconstruction of our Bayer sample deviates by {worst} counts at ({worstAt.X},{worstAt.Y}).");
  }

  private static byte[] _DecodeRggb16WithFfmpeg(MediaStreamInfo stream, CodedPacket packet, int width, int height) {
    var avi = VideoIO.Mux<AviWriter>([stream], [packet]);
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
      Assert.That(raw, Has.Length.EqualTo(width * height * 2));
      return raw;
    } finally {
      try { File.Delete(inputPath); } catch { /* best effort */ }
      try { File.Delete(outputPath); } catch { /* best effort */ }
    }
  }

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    // Zero, because these go through the AVI writer and an AVI's stream index is not a label: it is
    // written into every chunk identifier, so the streams have to run densely from nought.
    Index = 0,
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

  /// <summary>
  /// A mosaic that varies along both axes, with a different slope per sensor colour.
  /// </summary>
  /// <remarks>
  /// The per-colour slopes are the point. One shared ramp would leave three of CineForm's four coded
  /// components — (R-G), (B-G) and (G1-G2) — constant across the whole picture, and a constant plane
  /// is one a transposition or a mis-indexed walk reconstructs perfectly anyway. Giving R, G1, G2 and B
  /// slopes that do not cancel makes all four coded components vary in both axes, so each one has to be
  /// laid out correctly to come back.
  /// </remarks>
  private static RawImage _GradientRggb12(int width, int height) {
    var data = new byte[width * height * 2];
    for (var y = 0; y < height; y += 2)
    for (var x = 0; x < width; x += 2) {
      _Write(data, width, x, y, (ushort)(300 + 3 * x + 2 * y));
      _Write(data, width, x + 1, y, (ushort)(900 + 7 * x + 4 * y));
      _Write(data, width, x, y + 1, (ushort)(700 + 5 * x + 6 * y));
      _Write(data, width, x + 1, y + 1, (ushort)(1500 + 2 * x + 9 * y));
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
