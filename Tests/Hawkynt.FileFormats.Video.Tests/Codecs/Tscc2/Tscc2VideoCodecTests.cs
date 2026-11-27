using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Codecs.Tscc2;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>TechSmith Screen Codec 2 both ways, with FFmpeg as the oracle.</summary>
[TestFixture]
public sealed class Tscc2VideoCodecTests {

  [Test]
  [Category("Unit")]
  public void Tsc2IsDecodedAndEncoded() {
    var stream = _Stream(64, 48);
    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<Tscc2VideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<Tscc2VideoEncoder>());
    });
  }

  [Test]
  [Category("Unit")]
  public void CodesAreAssignedAsFFmpegAssignsThemFromLengths() {
    // Lengths 1, 2, 3, 3 give 0, 10, 110, 111; read least-significant bit first, the first bit read is the code's first.
    var vlc = new Tscc2Vlc([1, 2, 3, 3], [7, 8, 9, 10]);
    var writer = new Tscc2BitWriter();
    foreach (var symbol in new[] { 9, 7, 10, 8 })
      Assert.That(vlc.TryWrite(writer, symbol), Is.True);
    var bytes = writer.ToArray();
    // In order the bits are 110 0 111 10; the first eight, lowest first, make 11110011.
    Assert.That(Convert.ToString(bytes[0], 2).PadLeft(8, '0'), Is.EqualTo("11110011"));
  }

  [TestCase(96, 64)]
  [TestCase(70, 37)]
  [Category("Unit")]
  public void AYuv444PictureComesBackExactly(int width, int height) {
    var (_, ours, originals, _) = _Encode(width, height, 8);
    for (var i = 0; i < ours.Count; ++i)
      Assert.That(ours[i], Is.EqualTo(originals[i]), $"picture {i}");
  }

  [Test]
  [Category("Unit")]
  public void TheTransformIsUsedWhereItReproducesABlock() {
    var (_, _, _, packets) = _Encode(96, 64, 1);
    // A whole picture in raw samples would be 96*64*3 bytes and more; fills and the transform make it far smaller.
    Assert.That(packets[0].Data.Length, Is.LessThan(96 * 64 * 3 / 2));
  }

  [Test]
  [Category("Unit")]
  public void ARepeatFrameYieldsNoPicture() {
    var decoder = Tscc2VideoDecoder.Create(_Stream(16, 8));
    Assert.That(decoder.TryDecode(new(0, new byte[] { 0 }), out _), Is.False);
  }

  [TestCase(96, 64)]
  [TestCase(70, 37)]
  [Category("Conformance")]
  public void FFmpegReadsWhatTheEncoderWritesAsThisDecoderDoes(int width, int height) {
    FFmpegOracle.RequireAvailable();
    var (bytes, ours, _, _) = _Encode(width, height, 8);
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");
    try {
      File.WriteAllBytes(path, bytes);
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, "yuv444p");
      Assert.That(decoded, Is.True, detail);
      var size = width * height * 3;
      Assert.That(samples.Length / size, Is.EqualTo(ours.Count), "picture count");
      for (var i = 0; i < ours.Count; ++i)
        Assert.That(samples.AsSpan(i * size, size).ToArray(), Is.EqualTo(ours[i]), $"picture {i}");
    } finally {
      File.Delete(path);
    }
  }

  /// <summary>Opt-in: point <c>TSCC2_SAMPLES</c> at real TSCC2 films, such as FATE's <c>tscc/tsc2_16bpp.avi</c>.</summary>
  [Test]
  [Category("Conformance")]
  [Explicit("Needs real TSCC2 films; point TSCC2_SAMPLES at them.")]
  public void EveryPictureOfARealTscc2FilmMatchesFFmpeg()
    => RealSampleComparison.Run("TSCC2_SAMPLES", Tscc2VideoDecoder.Accepts, "yuv444p");

  // ==========================================================================================

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("tsc2"),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 10),
    FrameRate = new Rational(10, 1),
  };

  /// <summary>A screen: flat panels, a gradient, a block of text-like noise that moves, and a region that never changes.</summary>
  private static (byte[] Film, List<byte[]> Ours, List<byte[]> Originals, List<CodedPacket> Packets) _Encode(int width, int height, int count) {
    var encoder = Tscc2VideoEncoder.Create(_Stream(width, height));
    var stream = encoder.DescribeStream();
    var decoder = Tscc2VideoDecoder.Create(stream);
    var random = new Random(3);
    var packets = new List<CodedPacket>();
    var ours = new List<byte[]>();
    var originals = new List<byte[]>();
    for (var n = 0; n < count; ++n) {
      var pixels = new byte[width * height * 3];
      for (var plane = 0; plane < 3; ++plane)
        for (var y = 0; y < height; ++y)
          for (var x = 0; x < width; ++x) {
            byte value;
            if (y < height / 4)
              value = (byte)(40 + plane * 50);
            else if (x < width / 3)
              value = (byte)(64 + plane * 16);
            else if (x < 2 * width / 3)
              value = (byte)((x + n * 3 + plane) * 2);
            else
              value = (byte)(((x + y * 7 + n) * 13 + plane * 31) & 0xF0 | random.Next(4));
            pixels[plane * width * height + y * width + x] = value;
          }
      var picture = new RawImage { Width = width, Height = height, Format = PixelFormat.Yuv444P8, PixelData = pixels };
      encoder.TryEncode(picture, n, out var packet);
      packets.Add(packet);
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      ours.Add(decoded.PixelData.ToArray());
      originals.Add(pixels);
    }
    return (VideoIO.Mux<AviWriter>([stream], packets), ours, originals, packets);
  }
}
