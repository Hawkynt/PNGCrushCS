using System;
using System.Security.Cryptography;
using FileFormat.Core;

namespace FileFormat.Codecs.Dv.Tests;

/// <summary>SMPTE 370M encode/decode vectors generated independently with FFmpeg's C DV100 path.</summary>
/// <remarks>
/// The source planes are generated here rather than committed as multi-megabyte fixtures. The same
/// deterministic YUV422 planes were fed to FFmpeg 7.1.5 with <c>-cpuflags 0 -c:v dvvideo</c>. The first
/// digest is FFmpeg's coded packet; the second is FFmpeg's decoded planar YUV422 output. Matching both
/// makes the writer bit-exact with that oracle and the reader sample-exact on the resulting stream.
/// </remarks>
[TestFixture]
public class Dv100ConformanceTests {

  private static readonly (
    int Width, int Height, Rational Rate, int FrameSize, string Profile,
    string EncodedDigest, string DecodedDigest)[] _Profiles = [
    (1280, 1080, new Rational(30000, 1001), 480000, "DVCPRO HD 1080/60i",
      "d0eaceb659898b2aea8e591d6114ff2a", "8d549b5fe0d66b4386312d606ded3589"),
    (1440, 1080, new Rational(25, 1), 576000, "DVCPRO HD 1080/50i",
      "37477daa93da6bfa337fb05f908037fb", "db22a8f42ca300e3122a8f2d4389ddba"),
    (960, 720, new Rational(60000, 1001), 240000, "DVCPRO HD 720/60p",
      "b3a44659fc290889828c5e463743c495", "1bc7cbf340a9d0c44aeabe0a5543e429"),
    (960, 720, new Rational(50, 1), 288000, "DVCPRO HD 720/50p",
      "83ec6d228b9b3a6c9424aa70ae3e29eb", "1bc7cbf340a9d0c44aeabe0a5543e429"),
  ];

  [Test]
  [Category("Unit")]
  public void EveryDv100ProfileMatchesTheFFmpegCEncoderAndDecoder() {
    foreach (var vector in _Profiles) {
      var source = _Planes(vector.Width, vector.Height);
      var encoder = DvVideoEncoder.Create(new() {
        Index = 0,
        Kind = MediaStreamKind.Video,
        Width = vector.Width,
        Height = vector.Height,
        FrameRate = vector.Rate,
      });

      var encoded = encoder.EncodePlanes(source, DvSampling.FourTwoTwo);
      var decoder = DvVideoDecoder.Create(new() {
        Index = 0,
        Kind = MediaStreamKind.Video,
        Width = vector.Width,
        Height = vector.Height,
      });
      var decoded = decoder.DecodePlanes(encoded, out var profile);

      Assert.Multiple(() => {
        Assert.That(encoded.Length, Is.EqualTo(vector.FrameSize), vector.Profile);
        Assert.That(profile.Name, Is.EqualTo(vector.Profile), vector.Profile);
        Assert.That(_Digest(encoded), Is.EqualTo(vector.EncodedDigest),
          $"{vector.Profile}: coded packet differs from FFmpeg's scalar/C DV100 encoder.");
        Assert.That(_Digest(decoded), Is.EqualTo(vector.DecodedDigest),
          $"{vector.Profile}: decoded planes differ from FFmpeg's decode of that packet.");
      });
    }
  }

  [Test]
  [Category("Unit")]
  public void Dv100PacketsAreIndependentKeyFrames() {
    foreach (var vector in _Profiles) {
      var image = _Image(_Planes(vector.Width, vector.Height));
      var encoder = DvVideoEncoder.Create(new() {
        Index = 7,
        Kind = MediaStreamKind.Video,
        Width = vector.Width,
        Height = vector.Height,
        FrameRate = vector.Rate,
      });

      Assert.That(encoder.TryEncode(image, 123, out var packet), Is.True, vector.Profile);
      Assert.Multiple(() => {
        Assert.That(packet.StreamIndex, Is.EqualTo(7), vector.Profile);
        Assert.That(packet.PresentationTimestamp, Is.EqualTo(123), vector.Profile);
        Assert.That(packet.DecodeTimestamp, Is.EqualTo(123), vector.Profile);
        Assert.That(packet.IsKeyFrame, Is.True,
          $"{vector.Profile}: DV has no P/B pictures or forward/backward picture references.");
      });
    }
  }

  private static DvPlanes _Planes(int width, int height) {
    var luma = new byte[width * height];
    var chromaWidth = width / 2;
    var cb = new byte[chromaWidth * height];
    var cr = new byte[chromaWidth * height];

    for (var y = 0; y < height; ++y) {
      var lumaRow = y * width;
      for (var x = 0; x < width; ++x)
        luma[lumaRow + x] = (byte)(16 + ((x * 13 + y * 7 + (x ^ y)) % 220));

      var chromaRow = y * chromaWidth;
      for (var x = 0; x < chromaWidth; ++x) {
        cb[chromaRow + x] = (byte)(16 + ((x * 5 + y * 11 + x * y % 31) % 224));
        cr[chromaRow + x] = (byte)(16 + ((x * 9 + y * 3 + (x ^ (y * 3))) % 224));
      }
    }

    return new() {
      Width = width,
      Height = height,
      ChromaWidth = chromaWidth,
      ChromaHeight = height,
      Luma = luma,
      Cb = cb,
      Cr = cr,
    };
  }

  private static RawImage _Image(DvPlanes planes) {
    var data = new byte[planes.Luma.Length + planes.Cb.Length + planes.Cr.Length];
    planes.Luma.CopyTo(data, 0);
    planes.Cb.CopyTo(data, planes.Luma.Length);
    planes.Cr.CopyTo(data, planes.Luma.Length + planes.Cb.Length);
    return new() {
      Width = planes.Width,
      Height = planes.Height,
      Format = PixelFormat.Yuv422P8,
      PixelData = data,
      ColorInfo = RawImageColorInfo.Bt709Limited,
    };
  }

  private static string _Digest(ReadOnlySpan<byte> data)
    => Convert.ToHexString(MD5.HashData(data)).ToLowerInvariant();

  private static string _Digest(DvPlanes planes) {
    using var md5 = MD5.Create();
    md5.TransformBlock(planes.Luma, 0, planes.Luma.Length, null, 0);
    md5.TransformBlock(planes.Cb, 0, planes.Cb.Length, null, 0);
    md5.TransformFinalBlock(planes.Cr, 0, planes.Cr.Length);
    return Convert.ToHexString(md5.Hash!).ToLowerInvariant();
  }
}
