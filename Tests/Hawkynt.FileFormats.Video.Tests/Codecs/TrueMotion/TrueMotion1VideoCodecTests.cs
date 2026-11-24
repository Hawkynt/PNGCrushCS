using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>TrueMotion 1 header, tables, both depths, inter frames and writing.</summary>
[TestFixture]
public sealed class TrueMotion1VideoCodecTests {

  [TestCase("DUCK")]
  [TestCase("PVEZ")]
  [Category("Unit")]
  public void DecoderIsRegisteredForBothFourCcs(string fourCc) {
    var stream = _Stream(16, 8, fourCc);
    Assert.Multiple(() => {
      Assert.That(TrueMotion1VideoDecoder.Accepts(stream), Is.True);
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<TrueMotion1VideoDecoder>());
    });
  }

  [Test]
  [Category("Unit")]
  public void EncoderIsRegisteredForDuck() {
    var stream = _Stream(16, 8, "DUCK");
    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True);
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<TrueMotion1VideoEncoder>());
    });
  }

  [Test]
  [Category("Unit")]
  public void VectorTablesAreFFmpegsByteForByte() {
    // Lengths and group counts of pc_tbl2/3/4 in truemotion1data.h.
    Assert.Multiple(() => {
      Assert.That(TrueMotion1Tables.VectorTables.Select(static t => t.Length), Is.EqualTo(new[] { 808, 768, 785 }));
      foreach (var table in TrueMotion1Tables.VectorTables) {
        var groups = 0;
        for (var at = 0; at < table.Length; at += 1 + table[at] / 2)
          ++groups;
        Assert.That(groups, Is.EqualTo(256));
      }
      Assert.That(TrueMotion1Tables.VectorTables[0][..10], Is.EqualTo(new byte[] { 8, 0, 0, 0, 0, 8, 0, 0, 0, 0 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void SkinnyLumaDeltasAreHalvedRoundingNegativeOddsDown() {
    var tables = new TrueMotion1Format.PredictorTables();
    tables.SelectDeltas(2);
    tables.Generate(TrueMotion1Tables.VectorTables[0], TrueMotion1Format.Algorithm.Rgb24);
    // ydt3 = { 4, -6, 20, ... } halved to { 2, -3, 10, ... }; group 0 is four (0,0) pairs.
    var entry = tables.Luma[0] >> 1;
    Assert.That(entry & 0xff, Is.EqualTo(2u));
  }

  [Test]
  [Category("Unit")]
  public void HeaderSurvivesTheXorChain() {
    var header = new TrueMotion1Format.Header(14, 8, 1, 2, 120, 160, 0x1234, 2, 2, TrueMotion1Format.FlagInterFrame, 0);
    var data = new byte[20];
    data[14] = 0x5A;
    TrueMotion1Format.WriteHeader(data, header, data[14]);
    var read = TrueMotion1Format.ReadHeader(data);
    Assert.Multiple(() => {
      Assert.That(data[0], Is.GreaterThanOrEqualTo(0x10));
      Assert.That(data[14], Is.EqualTo(0x5A));
      Assert.That(read, Is.EqualTo(header));
    });
  }

  [Test]
  [Category("Unit")]
  public void MalformedHeadersAreRefused() {
    var decoder = TrueMotion1VideoDecoder.Create(_Stream(16, 8, "DUCK"));
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, new byte[] { 0x05, 0, 0 }), out _));
      Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, new byte[] { 0xC1, 0, 0 }), out _));
      Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, Array.Empty<byte>()), out _));
    });
  }

  [Test]
  [Category("Unit")]
  public void AnInterFrameWithoutAPictureIsRefused() {
    var packet = _Packet(compression: 8, flags: TrueMotion1Format.FlagInterFrame, width: 8, height: 4, new byte[64]);
    var decoder = TrueMotion1VideoDecoder.Create(_Stream(8, 4, "DUCK"));
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, packet), out _));
  }

  [Test]
  [Category("Unit")]
  public void AnIndexStreamThatEndsEarlyIsRefusedRatherThanLeftHalfDrawn() {
    var packet = _Packet(compression: 8, flags: TrueMotion1Format.FlagKeyFrame, width: 8, height: 4, new byte[2]);
    var decoder = TrueMotion1VideoDecoder.Create(_Stream(8, 4, "DUCK"));
    Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, packet), out _));
  }

  [Test]
  [Category("Unit")]
  public void SpritesAndBadGeometryAreRefusedByName() {
    var decoder = TrueMotion1VideoDecoder.Create(_Stream(8, 4, "DUCK"));
    Assert.Multiple(() => {
      Assert.Throws<NotSupportedException>(() => decoder.TryDecode(
        new(0, _Packet(8, TrueMotion1Format.FlagSprite | TrueMotion1Format.FlagKeyFrame, 8, 4, new byte[64])), out _));
      Assert.Throws<NotSupportedException>(() => decoder.TryDecode(
        new(0, _Packet(8, TrueMotion1Format.FlagKeyFrame, 6, 4, new byte[64])), out _));
      Assert.Throws<NotSupportedException>(() => decoder.TryDecode(
        new(0, _Packet(8, TrueMotion1Format.FlagKeyFrame, 8, 6, new byte[64])), out _));
      Assert.Throws<NotSupportedException>(() => TrueMotion1VideoEncoder.Create(_Stream(6, 4, "DUCK")));
      Assert.Throws<NotSupportedException>(() => TrueMotion1VideoEncoder.Create(_Stream(8, 6, "DUCK")));
    });
  }

  [Test]
  [Category("Unit")]
  public void AFrameThatCodesNothingRepeatsThePictureBeforeIt() {
    var encoder = TrueMotion1VideoEncoder.Create(_Stream(16, 8, "DUCK"));
    var picture = _Gradient(16, 8, seed: 3);
    encoder.TryEncode(picture, 0, out var key);

    var decoder = TrueMotion1VideoDecoder.Create(encoder.DescribeStream());
    decoder.TryDecode(key, out var first);
    Assert.That(decoder.TryDecode(new(0, _Packet(0, TrueMotion1Format.FlagInterFrame, 16, 8, [0])), out var repeated), Is.True);
    Assert.That(repeated.PixelData, Is.EqualTo(first.PixelData));
  }

  [TestCase(16, 0, 0, 0)]
  [TestCase(16, 255, 255, 255)]
  [TestCase(16, 200, 64, 32)]
  [TestCase(24, 0, 0, 0)]
  [TestCase(24, 200, 64, 32)]
  [Category("Unit")]
  public void ASolidPictureComesBackAtTheCodecsDepth(int bits, int red, int green, int blue) {
    // Both predictors start at black, so the first row climbs to the colour a bounded delta at a
    // time; past that ramp every pixel is the colour at the codec's depth.
    var picture = _Solid(32, 16, (byte)red, (byte)green, (byte)blue);
    var decoded = _RoundTrip(picture, bits, frames: 1)[0];
    var tolerance = bits == 16 ? 8 : 4;
    var worst = 0;
    for (var y = 4; y < 16; ++y)
    for (var i = 8 * 3; i < 32 * 3; ++i)
      worst = Math.Max(worst, Math.Abs(picture.PixelData[y * 32 * 3 + i] - decoded.PixelData[y * 32 * 3 + i]));
    Assert.That(worst, Is.LessThanOrEqualTo(tolerance));
  }

  [TestCase(16, 28.0)]
  [TestCase(24, 30.0)]
  [Category("Unit")]
  public void AGradientWithTextureComesBackAtAReasonableFidelity(int bits, double minimumPsnr) {
    var picture = _Gradient(64, 48, seed: 1);
    var decoded = _RoundTrip(picture, bits, frames: 1)[0];
    var psnr = _Psnr(picture.PixelData, decoded.PixelData);
    TestContext.Out.WriteLine($"{bits}-bit PSNR {psnr:F2} dB");
    Assert.That(psnr, Is.GreaterThanOrEqualTo(minimumPsnr));
  }

  [TestCase(16)]
  [TestCase(24)]
  [Category("Unit")]
  public void AStaticSecondPictureIsAnInterFrameThatCopiesEveryBlock(int bits) {
    var picture = _Gradient(32, 16, seed: 7);
    var encoder = TrueMotion1VideoEncoder.Create(_Stream(32, 16, "DUCK", bits));
    encoder.TryEncode(picture, 0, out var first);
    encoder.TryEncode(picture, 1, out var second);

    var decoder = TrueMotion1VideoDecoder.Create(encoder.DescribeStream());
    decoder.TryDecode(first, out var a);
    decoder.TryDecode(second, out var b);
    Assert.Multiple(() => {
      Assert.That(second.IsKeyFrame, Is.False);
      Assert.That(second.Data.Length, Is.LessThan(first.Data.Length / 2));
      Assert.That(b.PixelData, Is.EqualTo(a.PixelData));
    });
  }

  [TestCase(16)]
  [TestCase(24)]
  [Category("Unit")]
  public void APartlyChangedPictureStaysCloseAcrossInterFrames(int bits) {
    var pictures = Enumerable.Range(0, 5).Select(i => _MovingSquare(32, 16, i)).ToList();
    var encoder = TrueMotion1VideoEncoder.Create(_Stream(32, 16, "DUCK", bits));
    var decoder = TrueMotion1VideoDecoder.Create(encoder.DescribeStream());
    foreach (var (picture, i) in pictures.Select((p, i) => (p, i))) {
      encoder.TryEncode(picture, i, out var packet);
      decoder.TryDecode(packet, out var decoded);
      Assert.That(_Psnr(picture.PixelData, decoded.PixelData), Is.GreaterThan(28.0), $"picture {i}");
    }
  }

  [TestCase(16)]
  [TestCase(24)]
  [Category("Conformance")]
  public void FFmpegDecodesWhatTheEncoderWritesToTheSamePictureAsThisDecoder(int bits) {
    FFmpegOracle.RequireAvailable();
    var pictures = Enumerable.Range(0, 14).Select(i => i % 5 == 0 ? _Gradient(48, 32, i) : _MovingSquare(48, 32, i)).ToList();
    var encoder = TrueMotion1VideoEncoder.Create(_Stream(48, 32, "DUCK", bits));
    var decoder = TrueMotion1VideoDecoder.Create(encoder.DescribeStream());

    var packets = new List<CodedPacket>();
    var ours = new List<byte[]>();
    foreach (var (picture, i) in pictures.Select((p, i) => (p, (long)i))) {
      encoder.TryEncode(picture, i, out var packet);
      packets.Add(packet);
      decoder.TryDecode(packet, out var decoded);
      ours.Add(decoded.PixelData);
    }

    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");
    try {
      File.WriteAllBytes(path, VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets));
      var (decoded, detail, samples) = FFmpegOracle.TryDecodePictures(path, 48, 32, pictures.Count);
      Assert.That(decoded, Is.True, detail);
      for (var i = 0; i < ours.Count; ++i)
        Assert.That(samples.AsSpan(i * 48 * 32 * 3, 48 * 32 * 3).ToArray(), Is.EqualTo(ours[i]), $"picture {i}");
    } finally {
      File.Delete(path);
    }
  }

  // ==========================================================================================

  private static List<RawImage> _RoundTrip(RawImage picture, int bits, int frames) {
    var encoder = TrueMotion1VideoEncoder.Create(_Stream(picture.Width, picture.Height, "DUCK", bits));
    var decoder = TrueMotion1VideoDecoder.Create(encoder.DescribeStream());
    var result = new List<RawImage>();
    for (var i = 0; i < frames; ++i) {
      Assert.That(encoder.TryEncode(picture, i, out var packet), Is.True);
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      result.Add(decoded);
    }
    return result;
  }

  private static byte[] _Packet(int compression, int flags, int width, int height, byte[] payload) {
    var rgb24 = TrueMotion1Format.CompressionTypes[compression].Algorithm == TrueMotion1Format.Algorithm.Rgb24;
    var header = new TrueMotion1Format.Header(
      14, compression, 0, 1, height, rgb24 ? width * 2 : width, 0, 2, 2, flags, 0);
    var data = new byte[14 + payload.Length];
    payload.CopyTo(data, 14);
    TrueMotion1Format.WriteHeader(data, header, data[14]);
    return data;
  }

  private static MediaStreamInfo _Stream(int width, int height, string fourCc, int bits = 0) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(fourCc),
    Handler = CodecTag.FromCharacters(fourCc),
    Width = width,
    Height = height,
    BitsPerPixel = bits,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _Solid(int width, int height, byte red, byte green, byte blue) {
    var pixels = new byte[width * height * 3];
    for (var i = 0; i < pixels.Length; i += 3) {
      pixels[i] = red;
      pixels[i + 1] = green;
      pixels[i + 2] = blue;
    }
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static RawImage _Gradient(int width, int height, int seed) {
    var random = new Random(seed);
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)Math.Clamp(x * 255 / width + random.Next(-6, 7), 0, 255);
      pixels[at + 1] = (byte)Math.Clamp(y * 255 / height + random.Next(-6, 7), 0, 255);
      pixels[at + 2] = (byte)Math.Clamp(128 + (x - y) * 2 + random.Next(-6, 7), 0, 255);
    }
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static RawImage _MovingSquare(int width, int height, int step) {
    var picture = _Solid(width, height, 40, 90, 160);
    for (var y = 4; y < 12; ++y)
    for (var x = 4 + step * 2; x < 12 + step * 2 && x < width; ++x) {
      var at = (y * width + x) * 3;
      picture.PixelData[at] = 230;
      picture.PixelData[at + 1] = 200;
      picture.PixelData[at + 2] = 20;
    }
    return picture;
  }

  private static double _Psnr(byte[] a, byte[] b) {
    var mse = a.Zip(b, static (x, y) => (double)(x - y) * (x - y)).Average();
    return mse == 0 ? 99 : 10 * Math.Log10(255.0 * 255.0 / mse);
  }
}
