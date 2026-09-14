using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.DnxHd.Tests;

[TestFixture]
public sealed class DnxHdVideoEncoderTests {

  [Test]
  [Category("Unit")]
  public void Progressive720pPacketHasTheAnnexCSizeAndRoundTripsThroughOwnDecoder() {
    var frame = _Flat(960, 720);
    var encoder = DnxHdVideoEncoder.Create(_Stream(frame.Width, frame.Height));

    Assert.That(encoder.TryEncode(frame, 17, out var packet), Is.True);

    Assert.Multiple(() => {
      Assert.That(packet.Data.Length, Is.EqualTo(212_992), "CID 1258 has a fixed 212992-byte coding unit");
      Assert.That(packet.IsKeyFrame, Is.True, "VC-3 is intra-frame only");
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(17));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(17), "an intra-only codec never reorders pictures");
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(packet.Data.Span[0x28..]), Is.EqualTo(1258));
      Assert.That(packet.Data.Span[^4..].ToArray(), Is.EqualTo(new byte[] { 0x60, 0x0D, 0xC0, 0xDE }));
    });

    var decoder = DnxHdVideoDecoder.Create(encoder.DescribeStream());
    var planes = decoder.DecodePlanes(packet.Data, out var header);
    Assert.Multiple(() => {
      Assert.That(header.CompressionIdValue, Is.EqualTo(1258));
      Assert.That(header.BitDepth, Is.EqualTo(8));
      Assert.That(header.SubSampling, Is.Zero);
      Assert.That(planes.Luma, Is.All.EqualTo(128));
      Assert.That(planes.Cb, Is.All.EqualTo(128));
      Assert.That(planes.Cr, Is.All.EqualTo(128));
    });
  }

  [Test]
  [Category("Unit")]
  public void RegistryExposesOnlyStandardProgressiveRastersForTheWriter() {
    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CreateEncoder(_Stream(1280, 720)), Is.TypeOf<DnxHdVideoEncoder>());
      Assert.That(DnxHdVideoEncoder.Accepts(_Stream(1920, 1080)), Is.True);
      Assert.That(DnxHdVideoEncoder.Accepts(_Stream(1440, 1080)), Is.True);
      Assert.That(DnxHdVideoEncoder.Accepts(_Stream(960, 720)), Is.True);
      Assert.That(DnxHdVideoEncoder.Accepts(_Stream(1920, 1088)), Is.False,
        "classic DNxHD profiles state fixed rasters; this is not DNxHR");
    });
  }

  [Test]
  [Category("Oracle")]
  public void FFmpegDecodesTheWrittenEntropyStreamToTheSamePlanes() {
    FFmpegOracle.RequireAvailable();

    const int width = 960;
    const int height = 720;
    var frame = _Pattern(width, height);
    var encoder = DnxHdVideoEncoder.Create(_Stream(width, height));
    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);

    var ownDecoder = DnxHdVideoDecoder.Create(encoder.DescribeStream());
    var own = ownDecoder.DecodePlanes(packet.Data, out _);
    var expected = new byte[width * height * 2];
    var lumaSamples = width * height;
    var chromaSamples = width / 2 * height;
    for (var i = 0; i < lumaSamples; ++i)
      expected[i] = (byte)own.Luma[i];
    for (var i = 0; i < chromaSamples; ++i) {
      expected[lumaSamples + i] = (byte)own.Cb[i];
      expected[lumaSamples + chromaSamples + i] = (byte)own.Cr[i];
    }

    var directory = Directory.CreateTempSubdirectory("dnxhd-oracle");
    try {
      var coded = Path.Combine(directory.FullName, "frame.dnxhd");
      var decoded = Path.Combine(directory.FullName, "frame.yuv");
      File.WriteAllBytes(coded, packet.Data.ToArray());

      var startInfo = new ProcessStartInfo(FFmpegOracle.ExecutablePath!) {
        RedirectStandardError = true,
        UseShellExecute = false,
      };
      foreach (var argument in new[] {
        "-hide_banner", "-loglevel", "error", "-y", "-f", "dnxhd", "-i", coded,
        "-frames:v", "1", "-f", "rawvideo", "-pix_fmt", "yuv422p", decoded,
      })
        startInfo.ArgumentList.Add(argument);

      using var process = Process.Start(startInfo)!;
      var diagnostics = process.StandardError.ReadToEnd();
      Assert.That(process.WaitForExit(60_000), Is.True, "ffmpeg timed out decoding one DNxHD frame");
      Assert.That(process.ExitCode, Is.Zero, $"ffmpeg refused the DNxHD coding unit: {diagnostics}");

      var actual = File.ReadAllBytes(decoded);
      Assert.That(actual, Has.Length.EqualTo(expected.Length));

      var worst = 0;
      long total = 0;
      for (var i = 0; i < expected.Length; ++i) {
        var difference = Math.Abs(expected[i] - actual[i]);
        worst = Math.Max(worst, difference);
        total += difference;
      }

      Assert.Multiple(() => {
        Assert.That(worst, Is.LessThanOrEqualTo(6),
          "the exact IDCT here and ffmpeg's integer IDCT may differ by a few sample levels, not by a different bitstream interpretation");
        Assert.That(total / (double)expected.Length, Is.LessThan(0.15));
      });
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }

  private static MediaStreamInfo _Stream(int width, int height)
    => new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("AVdn"),
      TimeBase = new Rational(1, 25),
      FrameRate = new Rational(25, 1),
      Width = width,
      Height = height,
      BitsPerPixel = 16,
    };

  private static RawImage _Flat(int width, int height) {
    var data = new byte[width * height * 2];
    Array.Fill(data, (byte)128);
    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Yuv422P8,
      PixelData = data,
      ColorInfo = RawImageColorInfo.Bt709Limited,
    };
  }

  private static RawImage _Pattern(int width, int height) {
    var frame = _Flat(width, height);
    var data = frame.PixelData;
    var lumaSamples = width * height;
    var chromaWidth = width / 2;
    var chromaSamples = chromaWidth * height;

    for (var y = 0; y < 64; ++y)
      for (var x = 0; x < 64; ++x)
        data[y * width + x] = (byte)(32 + (x * 3 + y * 5) % 192);

    for (var y = 0; y < 64; ++y)
      for (var x = 0; x < 32; ++x) {
        data[lumaSamples + y * chromaWidth + x] = (byte)(64 + (x * 5 + y) % 128);
        data[lumaSamples + chromaSamples + y * chromaWidth + x] = (byte)(64 + (x + y * 3) % 128);
      }

    return frame;
  }
}
