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

/// <summary>Dxtory both ways, with FFmpeg as the oracle for the encoder's frames and for crafted frames of every layout.</summary>
[TestFixture]
public sealed class DxtoryVideoCodecTests {

  [Test]
  [Category("Unit")]
  public void XtorIsDecodedAndEncoded() {
    var stream = _Stream(64, 48);
    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<DxtoryVideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<DxtoryVideoEncoder>());
    });
  }

  private static IEnumerable<TestCaseData> Layouts() {
    foreach (var format in new[] { PixelFormat.Yuv420P8, PixelFormat.Yuv444P8, PixelFormat.Yuv410P8, PixelFormat.Rgb24 })
      foreach (var (width, height) in new[] { (64, 48), (37, 21), (38, 22), (41, 24) })
        yield return new TestCaseData(format, width, height).SetName($"{{m}}({format}, {width}x{height})");
  }

  [TestCaseSource(nameof(Layouts))]
  [Category("Unit")]
  public void APictureComesBackExactly(PixelFormat format, int width, int height) {
    var (_, ours, originals) = _Encode(format, width, height, 4);
    for (var i = 0; i < ours.Count; ++i)
      Assert.That(ours[i], Is.EqualTo(originals[i]), $"picture {i}");
  }

  [TestCaseSource(nameof(Layouts))]
  [Category("Conformance")]
  public void FFmpegReadsWhatTheEncoderWritesAsThisDecoderDoes(PixelFormat format, int width, int height) {
    FFmpegOracle.RequireAvailable();
    var (film, ours, _) = _Encode(format, width, height, 4);
    var written = format == PixelFormat.Yuv420P8 && height % 2 == 1 ? PixelFormat.Yuv444P8 : format;
    _CompareWithFFmpeg(film, ours, written == PixelFormat.Rgb24 ? "bgr24" : _Name(written));
  }

  /// <summary>Uncompressed frames of every layout, upright and flipped, filled with noise, read here and by FFmpeg.</summary>
  private static IEnumerable<TestCaseData> RawFrames() {
    var layouts = new (uint Type, string Pixels, int BytesPerPixelTimesFour)[] {
      (0x01000001, "bgr24", 12), (0x02000001, "yuv420p", 6), (0x03000001, "yuv410p", 5), (0x04000001, "yuv444p", 12),
      (0x17000001, "rgb565le", 8), (0x18000001, "rgb24", 8),
    };
    foreach (var (type, pixels, _) in layouts)
      foreach (var flip in new[] { false, true })
        // FFmpeg leaves samples unset for an odd-width 4:2:0 frame and for a 4:1:0 frame odd both ways, so those sizes are not compared.
        foreach (var (width, height) in type switch {
                   0x02000001 => new[] { (32, 16), (32, 17) },
                   0x03000001 => new[] { (32, 16), (34, 16), (32, 18) },
                   _ => new[] { (32, 16), (33, 17) },
                 })
          yield return new TestCaseData(type | (flip ? 0x20u : 0), pixels, width, height).SetName($"{{m}}(0x{type | (flip ? 0x20u : 0):X8}, {width}x{height})");
  }

  [TestCaseSource(nameof(RawFrames))]
  [Category("Conformance")]
  public void AnUncompressedFrameDecodesAsFFmpegDecodesIt(uint type, string pixels, int width, int height) {
    FFmpegOracle.RequireAvailable();
    var size = (type & 0xFF000000) switch {
      0x02000000 => width * height + 2 * ((width + 1) / 2) * ((height + 1) / 2),
      0x03000000 => width * height + 2 * ((width + 3) / 4) * ((height + 3) / 4) + 64,
      0x17000000 or 0x18000000 => width * height * 2,
      _ => width * height * 3,
    };
    var packet = new byte[16 + size];
    BinaryPrimitives.WriteUInt32BigEndian(packet, type);
    new Random((int)type ^ width).NextBytes(packet.AsSpan(16));
    _CompareCrafted(packet, pixels, width, height);
  }

  /// <summary>Version 2 frames of the 5- and 6-bit layouts, which the encoder does not write, made of random symbols.</summary>
  [TestCase(0x17000009u)]
  [TestCase(0x18000009u)]
  [TestCase(0x19000029u)]
  [Category("Conformance")]
  public void ACodedFiveBitFrameDecodesAsFFmpegDecodesIt(uint type) {
    FFmpegOracle.RequireAvailable();
    const int width = 40, height = 20;
    var slice = new byte[16 + width * height * 3];
    new Random((int)type).NextBytes(slice.AsSpan(16));
    BinaryPrimitives.WriteUInt32LittleEndian(slice, (uint)(slice.Length - 16));
    var packet = new byte[16 + 16 + slice.Length];
    BinaryPrimitives.WriteUInt32BigEndian(packet, type);
    BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(16), 1);
    BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(18), (uint)slice.Length);
    slice.CopyTo(packet, 32);
    _CompareCrafted(packet, "rgb24", width, height);
  }

  /// <summary>Opt-in: point <c>DXTORY_SAMPLES</c> at real Dxtory films, such as FATE's <c>dxtory/dxtory_mic.avi</c>.</summary>
  [Test]
  [Category("Conformance")]
  [Explicit("Needs real Dxtory films; point DXTORY_SAMPLES at them.")]
  public void EveryPictureOfARealDxtoryFilmMatchesFFmpeg()
    => RealSampleComparison.Run("DXTORY_SAMPLES", DxtoryVideoDecoder.Accepts, "yuv420p");

  // ==========================================================================================

  private static string _Name(PixelFormat format) => format switch {
    PixelFormat.Yuv420P8 => "yuv420p",
    PixelFormat.Yuv444P8 => "yuv444p",
    PixelFormat.Yuv410P8 => "yuv410p",
    _ => "bgr24",
  };

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("xtor"),
    Width = width,
    Height = height,
    BitsPerPixel = 24,
    TimeBase = new Rational(1, 30),
    FrameRate = new Rational(30, 1),
  };

  private static void _CompareCrafted(byte[] packet, string pixels, int width, int height) {
    var stream = _Stream(width, height);
    var ours = DxtoryVideoDecoder.Create(stream);
    ours.TryDecode(new(0, packet), out var picture);
    var expected = pixels is "rgb24" or "bgr24" && picture.Format == PixelFormat.Rgb24 && pixels == "bgr24" ? picture.ToRgb24() : picture.PixelData;
    _CompareWithFFmpeg(VideoIO.Mux<AviWriter>([stream], [new CodedPacket(0, packet, PresentationTimestamp: 0, IsKeyFrame: true)]), [expected], pixels);
  }

  private static void _CompareWithFFmpeg(byte[] film, List<byte[]> ours, string pixels) {
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");
    try {
      File.WriteAllBytes(path, film);
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, pixels);
      Assert.That(decoded, Is.True, detail);
      var size = ours[0].Length;
      Assert.That(samples.Length / size, Is.EqualTo(ours.Count), "picture count");
      for (var i = 0; i < ours.Count; ++i)
        Assert.That(samples.AsSpan(i * size, size).ToArray(), Is.EqualTo(ours[i]), $"picture {i}");
    } finally {
      File.Delete(path);
    }
  }

  /// <summary>Encodes pictures of noise and flat regions, and returns the film, this decoder's samples, and the originals in the same layout.</summary>
  private static (byte[] Film, List<byte[]> Ours, List<byte[]> Originals) _Encode(PixelFormat format, int width, int height, int count) {
    var encoder = DxtoryVideoEncoder.Create(_Stream(width, height));
    var random = new Random(width * 31 + height);
    var packets = new List<CodedPacket>();
    var originals = new List<byte[]>();
    var pictures = new List<RawImage>();
    for (var n = 0; n < count; ++n) {
      var size = format switch {
        PixelFormat.Yuv420P8 => width * height + 2 * ((width + 1) / 2) * ((height + 1) / 2),
        PixelFormat.Yuv410P8 => width * height + 2 * ((width + 3) / 4) * ((height + 3) / 4),
        _ => width * height * 3,
      };
      var pixels = new byte[size];
      for (var i = 0; i < size; ++i)
        pixels[i] = i % 7 == 0 ? (byte)random.Next(256) : (byte)((i / 5 + n) & 0xF0);
      var picture = new RawImage { Width = width, Height = height, Format = format, PixelData = pixels };
      encoder.TryEncode(picture, n, out var packet);
      packets.Add(packet);
      originals.Add(format == PixelFormat.Rgb24 ? _Swap(pixels)
        : format == PixelFormat.Yuv420P8 && height % 2 == 1 ? DxtoryVideoEncoder.RepeatChroma(picture).PixelData
        : pixels);
    }
    var stream = encoder.DescribeStream();
    var decoder = DxtoryVideoDecoder.Create(stream);
    var ours = new List<byte[]>();
    foreach (var packet in packets) {
      decoder.TryDecode(packet, out var decoded);
      ours.Add(decoded.PixelData.ToArray());
    }
    return (VideoIO.Mux<AviWriter>([stream], packets), ours, originals);
  }

  private static byte[] _Swap(byte[] rgb) {
    var bgr = (byte[])rgb.Clone();
    for (var i = 0; i < bgr.Length; i += 3)
      (bgr[i], bgr[i + 2]) = (bgr[i + 2], bgr[i]);
    return bgr;
  }
}
