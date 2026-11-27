using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using FileFormat.Flv;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>VP6 in its three forms: registration, coding, inter frames, alpha, and FFmpeg reading what is written.</summary>
[TestFixture]
public sealed class Vp6VideoCodecTests {

  [TestCase("VP60")]
  [TestCase("VP61")]
  [TestCase("VP62")]
  [TestCase("VP6F")]
  [TestCase("VP6A")]
  [Category("Unit")]
  public void EveryFormIsDecodedAndEncoded(string fourCc) {
    var stream = _Stream(32, 16, fourCc);
    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<Vp6VideoDecoder>());
      Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True);
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<Vp6VideoEncoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(stream).DescribeStream().Codec, Is.EqualTo(CodecTag.FromCharacters(fourCc)));
    });
  }

  [Test]
  [Category("Unit")]
  public void AHuffmanTreeHandsOutCodesInLeafOrder() {
    // Equal probabilities over the coefficient tree give the same tree FFmpeg builds for them.
    Span<byte> flat = stackalloc byte[11];
    flat.Fill(128);
    var table = FileFormat.Codecs.Vp6.Vp6Huffman.Build(flat, FileFormat.Codecs.Vp6.Vp6Tables.HuffmanCoefficientMap, 12);
    var bits = new FileFormat.Codecs.Vp6.Vp6BitReader(new byte[] { 0x00, 0x00, 0x00, 0x00 });
    Assert.That(table.Read(ref bits), Is.InRange(0, 11));
  }

  [TestCase("VP6F", 48, 32, 34.0)]
  [TestCase("VP62", 48, 32, 34.0)]
  [TestCase("VP6F", 33, 17, 32.0)]
  [Category("Unit")]
  public void APictureComesBackWithinTheQuantisersStep(string fourCc, int width, int height, double minimumPsnr) {
    var picture = _Gradient(width, height, 1);
    var encoder = Vp6VideoEncoder.Create(_Stream(width, height, fourCc));
    var decoder = Vp6VideoDecoder.Create(encoder.DescribeStream());
    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);
    Assert.That(packet.IsKeyFrame, Is.True);
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
    });
    var psnr = _Psnr(picture.ToRgb24(), decoded.ToRgb24());
    TestContext.Out.WriteLine($"{fourCc} {width}x{height}: {psnr:F2} dB, {packet.Data.Length} bytes");
    Assert.That(psnr, Is.GreaterThan(minimumPsnr));
  }

  [Test]
  [Category("Unit")]
  public void InterFramesFollowAMovingPictureAndStayClose() {
    var encoder = Vp6VideoEncoder.Create(_Stream(64, 48, "VP6F"));
    var decoder = Vp6VideoDecoder.Create(encoder.DescribeStream());
    var sizes = new List<int>();
    for (var i = 0; i < 8; ++i) {
      var picture = _Moving(64, 48, i);
      encoder.TryEncode(picture, i, out var packet);
      sizes.Add(packet.Data.Length);
      decoder.TryDecode(packet, out var decoded);
      Assert.That(_Psnr(picture.ToRgb24(), decoded.ToRgb24()), Is.GreaterThan(30.0), $"picture {i}");
    }
    TestContext.Out.WriteLine(string.Join(", ", sizes));
    Assert.That(sizes.Skip(1).Max(), Is.LessThan(sizes[0]), "inter frames of a moving picture should be smaller than its key frame");
  }

  [Test]
  [Category("Unit")]
  public void TheAlphaPlaneComesBackFromItsOwnBitstream() {
    var picture = _Gradient(32, 32, 4);
    var rgba = picture.ToRgba32();
    for (var y = 0; y < 32; ++y)
    for (var x = 0; x < 32; ++x)
      rgba[(y * 32 + x) * 4 + 3] = (byte)(x < 16 ? 0 : 255);
    var transparent = new RawImage { Width = 32, Height = 32, Format = PixelFormat.Rgba32, PixelData = rgba };

    var encoder = Vp6VideoEncoder.Create(_Stream(32, 32, "VP6A"));
    var decoder = Vp6VideoDecoder.Create(encoder.DescribeStream());
    encoder.TryEncode(transparent, 0, out var packet);
    decoder.TryDecode(packet, out var decoded);
    Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgba32));
    var alpha = Enumerable.Range(0, 32 * 32).Select(i => decoded.PixelData[i * 4 + 3]).ToArray();
    Assert.Multiple(() => {
      Assert.That(alpha.Where((_, i) => i % 32 < 12).Max(), Is.LessThan(16));
      Assert.That(alpha.Where((_, i) => i % 32 > 19).Min(), Is.GreaterThan(240));
    });
  }

  [Test]
  [Category("Unit")]
  public void FlashsCropByteCrossesTheFlvContainer() {
    var encoder = Vp6VideoEncoder.Create(_Stream(33, 17, "VP6F"));
    encoder.TryEncode(_Gradient(33, 17, 2), 0, out var packet);
    var flv = VideoIO.Mux<FlvWriter>([encoder.DescribeStream()], [packet]);
    var stream = VideoFormatRegistry.ReadStreams(flv).Single(static s => s.Kind == MediaStreamKind.Video);
    var back = VideoFormatRegistry.ReadPackets(flv, stream.Index).Single();
    var decoder = Vp6VideoDecoder.Create(stream);
    decoder.TryDecode(back, out var decoded);
    Assert.Multiple(() => {
      Assert.That(back.ContainerPrivateData.ToArray(), Is.EqualTo(new byte[] { (15 << 4) | 15 }));
      Assert.That(back.Data.ToArray(), Is.EqualTo(packet.Data.ToArray()));
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((33, 17)));
    });
  }

  [TestCase("VP6F", ".flv")]
  [TestCase("VP6A", ".flv")]
  [TestCase("VP62", ".avi")]
  [TestCase("VP60", ".avi")]
  [Category("Conformance")]
  public void FFmpegDecodesWhatTheEncoderWritesToTheSamePicturesAsThisDecoder(string fourCc, string extension) {
    FFmpegOracle.RequireAvailable();
    const int width = 50, height = 34;
    var encoder = Vp6VideoEncoder.Create(_Stream(width, height, fourCc));
    var decoder = Vp6VideoDecoder.Create(encoder.DescribeStream());
    var alpha = fourCc == "VP6A";
    var packets = new List<CodedPacket>();
    var ours = new List<byte[]>();
    for (var i = 0; i < 14; ++i) {
      var picture = i % 6 == 0 ? _Gradient(width, height, i) : _Moving(width, height, i);
      encoder.TryEncode(picture, i, out var packet);
      packets.Add(packet);
      decoder.TryDecode(packet, out _);
      var p = decoder.LastPlanes;
      ours.Add([.. p.Y, .. p.U, .. p.V, .. p.A ?? []]);
    }

    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + extension);
    try {
      var file = extension == ".flv"
        ? VideoIO.Mux<FlvWriter>([encoder.DescribeStream()], packets)
        : VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets);
      File.WriteAllBytes(path, file);
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, alpha ? "yuva420p" : "yuv420p");
      Assert.That(decoded, Is.True, detail);
      Assert.That(samples, Is.EqualTo(ours.SelectMany(static b => b).ToArray()));
    } finally {
      File.Delete(path);
    }
  }

  // ==========================================================================================

  private static MediaStreamInfo _Stream(int width, int height, string fourCc) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(fourCc),
    Handler = CodecTag.FromCharacters(fourCc),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _Gradient(int width, int height, int seed) {
    var random = new Random(seed);
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)Math.Clamp(x * 255 / width + random.Next(-4, 5), 0, 255);
      pixels[at + 1] = (byte)Math.Clamp(y * 255 / height + random.Next(-4, 5), 0, 255);
      pixels[at + 2] = (byte)Math.Clamp(128 + (x - y) * 2 + random.Next(-4, 5), 0, 255);
    }
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static RawImage _Moving(int width, int height, int step) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      var inside = x >= 4 + step * 2 && x < 20 + step * 2 && y >= 6 && y < 22;
      pixels[at] = inside ? (byte)220 : (byte)(40 + x);
      pixels[at + 1] = inside ? (byte)200 : (byte)(90 + y);
      pixels[at + 2] = inside ? (byte)30 : (byte)160;
    }
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static double _Psnr(byte[] a, byte[] b) {
    var mse = a.Zip(b, static (x, y) => (double)(x - y) * (x - y)).Average();
    return mse == 0 ? 99 : 10 * Math.Log10(255.0 * 255.0 / mse);
  }
}
