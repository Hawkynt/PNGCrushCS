using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>HuffYUV MT both ways: no real file exists, so FFmpeg is held to the slices this encoder writes.</summary>
[TestFixture]
public sealed class HymtVideoCodecTests {

  [Test]
  [Category("Unit")]
  public void HymtIsDecodedAndEncoded() {
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(64, 48, 24));
    Assert.Multiple(() => {
      Assert.That(encoder, Is.InstanceOf<HymtVideoEncoder>());
      Assert.That(VideoFormatRegistry.CreateDecoder(encoder.DescribeStream()), Is.InstanceOf<HymtVideoDecoder>());
    });
  }

  [TestCase(64, 48)]
  [TestCase(40, 31)]
  [Category("Unit")]
  public void ABgrPictureComesBackExactly(int width, int height) {
    var (_, ours, originals) = _Encode(width, height, 24);
    for (var i = 0; i < ours.Count; ++i)
      Assert.That(ours[i], Is.EqualTo(originals[i]), $"picture {i}");
  }

  /// <summary>4:2:2 comes back from this package's HuffYUV as RGB; it must be the RGB a plain HuffYUV frame of the same picture gives.</summary>
  [TestCase(66, 45)]
  [TestCase(64, 48)]
  [Category("Unit")]
  public void A422PictureDecodesAsPlainHuffYuvDoes(int width, int height) {
    var (_, ours, originals) = _Encode(width, height, 16);
    var plainStream = new MediaStreamInfo {
      Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("HFYU"), Width = width, Height = height, BitsPerPixel = 16,
      TimeBase = new Rational(1, 25), FrameRate = new Rational(25, 1),
    };
    var plain = HuffYuvEncoder.Create(plainStream, HuffYuvPredictionMethod.Left);
    var decoder = HuffYuvDecoder.Create(plain.DescribeStream());
    for (var i = 0; i < ours.Count; ++i) {
      plain.TryEncode(new RawImage { Width = width, Height = height, Format = PixelFormat.Yuv422P8, PixelData = originals[i] }, i, out var packet);
      decoder.TryDecode(packet, out var expected);
      Assert.That(ours[i], Is.EqualTo(expected.PixelData), $"picture {i}");
    }
  }

  /// <summary>
  /// FFmpeg reads the slices back to the very pictures given. For packed colour it decodes without its vector
  /// routines: the x86 left prediction writes a pixel past the row it fills, and where FFmpeg's rows have no
  /// padding that pixel is the first of the slice below, already decoded. The plain C routine, which the
  /// vector one stands in for, writes exactly the row.
  /// </summary>
  [TestCase(64, 48, 24, "bgr24")]
  [TestCase(40, 31, 24, "bgr24")]
  [TestCase(64, 48, 16, "yuv422p")]
  [TestCase(66, 45, 16, "yuv422p")]
  [Category("Conformance")]
  public void FFmpegReadsWhatTheEncoderWritesToThePicturesGiven(int width, int height, int bits, string pixels) {
    FFmpegOracle.RequireAvailable();
    var (film, _, originals) = _Encode(width, height, bits);
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");
    try {
      File.WriteAllBytes(path, film);
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, pixels, withoutSimd: bits == 24);
      Assert.That(decoded, Is.True, detail);
      var size = originals[0].Length;
      Assert.That(samples.Length / size, Is.EqualTo(originals.Count), "picture count");
      for (var i = 0; i < originals.Count; ++i)
        Assert.That(samples.AsSpan(i * size, size).ToArray(), Is.EqualTo(originals[i]), $"picture {i}");
    } finally {
      File.Delete(path);
    }
  }

  // ==========================================================================================

  private static MediaStreamInfo _Stream(int width, int height, int bits) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("HYMT"),
    Width = width,
    Height = height,
    BitsPerPixel = bits,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static (byte[] Film, List<byte[]> Ours, List<byte[]> Originals) _Encode(int width, int height, int bits) {
    var encoder = HymtVideoEncoder.Create(_Stream(width, height, bits));
    var format = bits == 16 ? PixelFormat.Yuv422P8 : PixelFormat.Bgr24;
    var size = bits == 16 ? width * height * 2 : width * height * 3;
    var random = new Random(3);
    var pictures = new List<RawImage>();
    var packets = new List<CodedPacket>();
    for (var n = 0; n < 3; ++n) {
      var data = new byte[size];
      for (var i = 0; i < size; ++i)
        data[i] = i % 13 == 0 ? (byte)random.Next(256) : (byte)((i / 7 + n * 3) & 0xFC);
      var picture = new RawImage { Width = width, Height = height, Format = format, PixelData = data };
      pictures.Add(picture);
      encoder.TryEncode(picture, n, out var packet);
      packets.Add(packet);
    }
    var stream = encoder.DescribeStream();
    var decoder = HymtVideoDecoder.Create(stream);
    var ours = new List<byte[]>();
    var originals = new List<byte[]>();
    for (var n = 0; n < packets.Count; ++n) {
      decoder.TryDecode(packets[n], out var decoded);
      ours.Add(bits == 24 && decoded.Format != PixelFormat.Bgr24 ? FastRawImageConverter.Convert(decoded, PixelFormat.Bgr24).PixelData : decoded.PixelData);
      originals.Add(pictures[n].PixelData);
    }
    return (VideoIO.Mux<AviWriter>([stream], packets), ours, originals);
  }
}
