using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.Ea;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>The four Electronic Arts encoders: back through this package's decoders, and through FFmpeg's.</summary>
[TestFixture]
public sealed class EaVideoEncoderTests {

  private const int _WIDTH = 96, _HEIGHT = 64, _PICTURES = 14;

  [TestCase("tqi ", typeof(EaTqiVideoEncoder))]
  [TestCase("tgq ", typeof(EaTgqVideoEncoder))]
  [TestCase("mad ", typeof(EaMadVideoEncoder))]
  [TestCase("tgv ", typeof(EaTgvVideoEncoder))]
  [Category("Unit")]
  public void TheRegistryFindsTheEncoder(string tag, Type expected)
    => Assert.That(VideoFormatRegistry.CreateEncoder(_Stream(tag)), Is.InstanceOf(expected));

  [TestCase("tqi ", 32.0)]
  [TestCase("tgq ", 30.0)]
  [TestCase("mad ", 30.0)]
  [Category("Unit")]
  public void PicturesComeBackCloseThroughThisDecoder(string tag, double minimumPsnr) {
    var (_, ours, originals) = _Encode(tag);
    for (var i = 0; i < ours.Count; ++i)
      Assert.That(_LumaPsnr(originals[i], ours[i]), Is.GreaterThan(minimumPsnr), $"picture {i}");
  }

  [Test]
  [Category("Unit")]
  public void ATgvPictureInThePaletteComesBackExactly() {
    var (_, ours, originals) = _Encode("tgv ", palettised: true);
    for (var i = 0; i < ours.Count; ++i)
      Assert.That(ours[i], Is.EqualTo(originals[i]), $"picture {i}");
  }

  [Test]
  [Category("Unit")]
  public void TheContainerCarriesTheNewCodecs() {
    foreach (var tag in new[] { "tqi ", "tgq ", "mad " }) {
      var (bytes, ours, _) = _Encode(tag);
      var stream = VideoFormatRegistry.ReadStreams(bytes).First(static s => s.Kind == MediaStreamKind.Video);
      var packets = VideoFormatRegistry.ReadPackets(bytes, stream.Index).ToList();
      Assert.Multiple(() => {
        Assert.That(stream.Codec.ToString().Trim(), Is.EqualTo(tag.Trim()), tag);
        Assert.That((stream.Width, stream.Height), Is.EqualTo((_WIDTH, _HEIGHT)), tag);
        Assert.That(packets, Has.Count.EqualTo(ours.Count), tag);
      });
    }
  }

  [TestCase("tqi ")]
  [TestCase("tgq ")]
  [TestCase("mad ")]
  [TestCase("tgv ")]
  [Category("Conformance")]
  public void FFmpegReadsWhatTheEncoderWritesAsThisDecoderDoes(string tag) => _CompareWithFFmpeg(tag, _PICTURES);

  /// <summary>FFmpeg looks five chunks deep for a sound header, so a silent film shorter than that is padded.</summary>
  [TestCase("tqi ")]
  [TestCase("mad ")]
  [TestCase("tgv ")]
  [Category("Conformance")]
  public void FFmpegReadsASilentFilmOfOnePicture(string tag) => _CompareWithFFmpeg(tag, 1);

  // ==========================================================================================

  private static void _CompareWithFFmpeg(string tag, int count) {
    FFmpegOracle.RequireAvailable();
    var tgv = tag == "tgv ";
    var (bytes, ours, _) = _Encode(tag, palettised: tgv, rgb: tgv, count: count);
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + (tag == "mad " ? ".mad" : tgv ? ".tgv" : ".uv"));
    try {
      File.WriteAllBytes(path, bytes);
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, tgv ? "rgb24" : "yuv420p");
      Assert.That(decoded, Is.True, detail);
      var size = ours[0].Length;
      Assert.That(samples.Length / size, Is.EqualTo(ours.Count), "picture count");
      for (var i = 0; i < ours.Count; ++i)
        Assert.That(samples.AsSpan(i * size, size).ToArray(), Is.EqualTo(ours[i]), $"picture {i}");
    } finally {
      File.Delete(path);
    }
  }

  private static MediaStreamInfo _Stream(string tag) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(tag),
    Width = _WIDTH,
    Height = _HEIGHT,
    TimeBase = new Rational(1, 15),
    FrameRate = new Rational(15, 1),
  };

  /// <summary>Encodes a moving picture, muxes it, and returns the film, this package's decoding of each packet, and the originals in the same layout.</summary>
  private static (byte[] Film, List<byte[]> Ours, List<byte[]> Originals) _Encode(string tag, bool palettised = false, bool rgb = false, int count = _PICTURES) {
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(tag));
    var stream = encoder.DescribeStream();
    var decoder = VideoFormatRegistry.CreateDecoder(stream);
    var packets = new List<CodedPacket>();
    var ours = new List<byte[]>();
    var originals = new List<byte[]>();
    for (var i = 0; i < count; ++i) {
      var picture = palettised ? _Palettised(i) : _Gradient(i);
      encoder.TryEncode(picture, i, out var packet);
      packets.Add(packet);
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True, $"picture {i}");
      if (palettised) {
        ours.Add(rgb ? decoded.ToRgb24() : decoded.PixelData.ToArray());
        originals.Add(rgb ? picture.ToRgb24() : picture.PixelData.ToArray());
      } else {
        ours.Add(decoded.PixelData.ToArray());
        originals.Add(FastRawImageConverter.Convert(picture, PixelFormat.Yuv420P8).PixelData.ToArray());
      }
    }
    return (VideoIO.Mux<EaWriter>([stream], packets), ours, originals);
  }

  private static RawImage _Gradient(int step) {
    var pixels = new byte[_WIDTH * _HEIGHT * 3];
    for (var y = 0; y < _HEIGHT; ++y)
      for (var x = 0; x < _WIDTH; ++x) {
        var at = (y * _WIDTH + x) * 3;
        // The left half moves and the right half stands still, so predicted pictures have both to do.
        var shift = x < _WIDTH / 2 ? step * 2 : 0;
        pixels[at] = (byte)(((x + shift) * 255 / _WIDTH) & 0xFF);
        pixels[at + 1] = (byte)(y * 255 / _HEIGHT);
        pixels[at + 2] = (byte)(128 + 60 * Math.Sin((x + y + shift) / 9.0));
      }
    return new() { Width = _WIDTH, Height = _HEIGHT, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static RawImage _Palettised(int step) {
    var palette = new byte[256 * 3];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)(255 - i);
      palette[i * 3 + 2] = (byte)(i * 7);
    }
    var pixels = new byte[_WIDTH * _HEIGHT];
    for (var y = 0; y < _HEIGHT; ++y)
      for (var x = 0; x < _WIDTH; ++x)
        pixels[y * _WIDTH + x] = (byte)(x < _WIDTH / 2 ? x + y + step : (x * y) >> 3);
    return new() { Width = _WIDTH, Height = _HEIGHT, Format = PixelFormat.Indexed8, PixelData = pixels, Palette = palette, PaletteCount = 256 };
  }

  private static double _LumaPsnr(byte[] a, byte[] b) {
    double sum = 0;
    for (var i = 0; i < _WIDTH * _HEIGHT; ++i) {
      double d = a[i] - b[i];
      sum += d * d;
    }
    return sum == 0 ? 99 : 10 * Math.Log10(255.0 * 255.0 * _WIDTH * _HEIGHT / sum);
  }
}
