using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using FileFormat.Matroska;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// Windows Media Video 8: registration, the extension header, coding round trips, FFmpeg reading what
/// is written here, and this decoder reading what FFmpeg writes.
/// </summary>
/// <remarks>
/// The tools FFmpeg's encoder never uses — IntraX8 pictures, quarter-sample motion, the adaptive 8x4
/// and 4x8 transforms, the chosen vector predictor and the row and column skip maps — are checked by
/// the opt-in real-film comparison: the eight films of <c>samples.ffmpeg.org/V-codecs/WMV8</c> and its
/// <c>j-frames</c> directory, 30,209 pictures, every one identical to FFmpeg's.
/// </remarks>
[TestFixture]
public sealed class Wmv2VideoCodecTests {

  private const int _TIMEOUT_MILLISECONDS = 60_000;

  [Test]
  [Category("Unit")]
  public void Wmv2HasADecoderAndAnEncoderOfItsOwn() {
    var stream = Wmv2VideoEncoder.Create(_Stream(64, 48)).DescribeStream();
    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<Wmv2VideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(_Stream(64, 48)), Is.InstanceOf<Wmv2VideoEncoder>());
      Assert.That(MsMpeg4VideoDecoder.Accepts(stream), Is.False);
      Assert.That(Wmv1VideoDecoder.Accepts(stream), Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void TheDescriptionCarriesTheExtensionHeaderAfterTheBitmapHeader() {
    var stream = Wmv2VideoEncoder.Create(_Stream(64, 48)).DescribeStream();
    var data = stream.CodecPrivateData.ToArray();
    Assert.Multiple(() => {
      Assert.That(data, Has.Length.EqualTo(44));
      Assert.That(BitConverter.ToUInt32(data, 0), Is.EqualTo(44u), "the header size counts the extension, as FFmpeg's muxers write it");
      // 25 frames a second, then the tools all off and one slice.
      Assert.That(data[40] >> 3, Is.EqualTo(25));
      Assert.That(data[40..], Is.EqualTo(Wmv2VideoDecoder.ExtensionOf(stream).ToArray()));
    });
  }

  [Test]
  [Category("Unit")]
  public void AStreamWithNoSizeIsRefused()
    => Assert.Throws<NotSupportedException>(() => Wmv2VideoDecoder.Create(_Stream(0, 0)));

  [Test]
  [Category("Unit")]
  public void AStreamWithoutItsExtensionHeaderIsRefused()
    => Assert.Throws<InvalidDataException>(() => Wmv2VideoDecoder.Create(_Stream(64, 48)));

  [Test]
  [Category("Unit")]
  public void AnExtensionHeaderOfNoughtSlicesIsRefused() {
    var stream = _Stream(64, 48);
    stream = new() {
      Index = 0, Kind = MediaStreamKind.Video, Codec = stream.Codec, Width = 64, Height = 48,
      CodecPrivateData = new byte[] { 0xC8, 0x00, 0x00, 0x00 },
    };
    Assert.Throws<InvalidDataException>(() => Wmv2VideoDecoder.Create(stream));
  }

  [Test]
  [Category("Unit")]
  public void APredictedPictureWhoseRowSkipMapSkipsEverythingIsNoPicture() {
    var encoder = Wmv2VideoEncoder.Create(_Stream(64, 48));
    var decoder = Wmv2VideoDecoder.Create(encoder.DescribeStream());
    encoder.TryEncode(_Moving(64, 48, 0), 0, out var intra);
    Assert.That(decoder.TryDecode(intra, out var first), Is.True);

    // Predicted, quantiser 8, the row layout, and every one of the three rows "all skipped".
    var bits = "1" + "01000" + "10" + "111";
    var skip = new byte[(bits.Length + 7) / 8 + 1];
    for (var i = 0; i < bits.Length; ++i)
      if (bits[i] == '1')
        skip[i >> 3] |= (byte)(0x80 >> (i & 7));

    Assert.That(decoder.TryDecode(new(0, skip, PresentationTimestamp: 1), out _), Is.False);

    // The stream goes on from the picture before it.
    encoder.TryEncode(_Moving(64, 48, 1), 1, out var predicted);
    Assert.That(decoder.TryDecode(predicted, out var second), Is.True);
    Assert.That(second.PixelData, Has.Length.EqualTo(first.PixelData.Length));
  }

  [TestCase(176, 144)]
  [TestCase(200, 150)]
  [TestCase(352, 288)]
  [Category("Unit")]
  public void PicturesComeBackClose(int width, int height) {
    var encoder = Wmv2VideoEncoder.Create(_Stream(width, height));
    var decoder = Wmv2VideoDecoder.Create(encoder.DescribeStream());
    var sizes = new List<int>();
    for (var i = 0; i < 8; ++i) {
      var picture = _Moving(width, height, i);
      encoder.TryEncode(picture, i, out var packet);
      sizes.Add(packet.Data.Length);
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Yuv420P8));
      Assert.That(_LumaPsnr(picture, decoded), Is.GreaterThan(30.0), $"picture {i}");
    }
    TestContext.Out.WriteLine(string.Join(", ", sizes));
    Assert.That(sizes.Skip(1).Max(), Is.LessThan(sizes[0]));
  }

  [TestCase(".avi", 176, 144)]
  [TestCase(".mkv", 200, 150)]
  [TestCase(".avi", 352, 288)]
  [TestCase(".avi", 34, 18)]
  [Category("Conformance")]
  public void FFmpegDecodesWhatTheEncoderWritesToTheSamePicturesAsThisDecoder(string extension, int width, int height) {
    FFmpegOracle.RequireAvailable();
    var encoder = Wmv2VideoEncoder.Create(_Stream(width, height));
    var decoder = Wmv2VideoDecoder.Create(encoder.DescribeStream());
    var packets = new List<CodedPacket>();
    var ours = new List<byte[]>();
    for (var i = 0; i < 26; ++i) {
      var picture = i % 9 == 0 ? _Noise(width, height, i) : i % 5 == 4 ? _Moving(width, height, i - 1) : _Moving(width, height, i);
      encoder.TryEncode(picture, i, out var packet);
      packets.Add(packet);
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      ours.Add(decoded.PixelData);
    }

    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + extension);
    try {
      File.WriteAllBytes(path, extension == ".avi"
        ? VideoIO.Mux<AviWriter>([encoder.DescribeStream()], packets)
        : VideoIO.Mux<MatroskaWriter>([encoder.DescribeStream()], packets));
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, "yuv420p");
      Assert.That(decoded, Is.True, detail);
      Assert.That(samples, Is.EqualTo(ours.SelectMany(static b => b).ToArray()));
    } finally {
      File.Delete(path);
    }
  }

  /// <summary>
  /// FFmpeg's encoder states the quarter-sample, adaptive-transform, J-frame and per-macroblock-table
  /// flags in its extension header, uses none of them, and with <c>+loop</c> deblocks its pictures.
  /// </summary>
  [TestCase(176, 144, false)]
  [TestCase(100, 60, true)]
  [TestCase(352, 288, true)]
  [Category("Conformance")]
  public void WhatFFmpegsEncoderWritesDecodesToItsOwnPictures(int width, int height, bool loopFilter) {
    FFmpegOracle.RequireAvailable();
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");
    try {
      _RunFFmpeg(
        "-f", "lavfi", "-i", $"testsrc2=size={width}x{height}:rate=25", "-frames:v", "30",
        "-c:v", "wmv2", "-g", "12", "-q:v", "4", "-flags", loopFilter ? "+bitexact+loop" : "+bitexact", path);

      var bytes = File.ReadAllBytes(path);
      var stream = VideoFormatRegistry.ReadStreams(bytes).Single(static s => s.Kind == MediaStreamKind.Video);
      var decoder = VideoFormatRegistry.CreateDecoder(stream);
      Assert.That(decoder, Is.InstanceOf<Wmv2VideoDecoder>());
      var ours = new List<byte>();
      foreach (var packet in VideoFormatRegistry.ReadPackets(bytes, stream.Index).Where(static p => p.Data.Length != 0))
        if (decoder.TryDecode(packet, out var picture))
          ours.AddRange(picture.PixelData);

      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, "yuv420p");
      Assert.That(decoded, Is.True, detail);
      Assert.That(ours.ToArray(), Is.EqualTo(samples));
      Assert.That(samples, Has.Length.EqualTo(30 * (width * height + 2 * ((width + 1) / 2) * ((height + 1) / 2))));
    } finally {
      File.Delete(path);
    }
  }

  /// <summary>Opt-in: point <c>WMV2_SAMPLES</c> at <c>samples.ffmpeg.org/V-codecs/WMV8</c> and its <c>j-frames</c> directory.</summary>
  [Test]
  [Category("Conformance")]
  [Explicit("Needs real WMV2 films; point WMV2_SAMPLES at them.")]
  public void EveryPictureOfARealWmv2FilmMatchesFFmpeg()
    => RealSampleComparison.Run("WMV2_SAMPLES", Wmv2VideoDecoder.Accepts, "yuv420p");

  // ==========================================================================================

  private static void _RunFFmpeg(params string[] arguments) {
    var startInfo = new ProcessStartInfo(FFmpegOracle.ExecutablePath!) {
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
    };
    foreach (var argument in new[] { "-hide_banner", "-loglevel", "error", "-y" }.Concat(arguments))
      startInfo.ArgumentList.Add(argument);

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("ffmpeg would not start");
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(_TIMEOUT_MILLISECONDS)) {
      try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
      Assert.Fail("ffmpeg timed out");
    }

    Assert.That(process.ExitCode, Is.Zero, string.Concat(stdout.Result, stderr.Result));
  }

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("WMV2"),
    Handler = CodecTag.FromCharacters("WMV2"),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _Moving(int width, int height, int step) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      var inside = x >= 10 + step * 3 && x < 50 + step * 3 && y >= 12 + step && y < 52 + step;
      pixels[at] = inside ? (byte)220 : (byte)(40 + x / 2);
      pixels[at + 1] = inside ? (byte)200 : (byte)(90 + y / 2);
      pixels[at + 2] = inside ? (byte)30 : (byte)(160 - x / 3);
    }
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  /// <summary>Busy enough to need escapes and long runs.</summary>
  private static RawImage _Noise(int width, int height, int seed) {
    var random = new Random(seed);
    var pixels = new byte[width * height * 3];
    random.NextBytes(pixels);
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static double _LumaPsnr(RawImage source, RawImage decoded) {
    var reference = FastRawImageConverter.Convert(source, PixelFormat.Yuv420P8).GetPlaneData(0).ToArray();
    var ours = decoded.GetPlaneData(0).ToArray();
    var mse = reference.Zip(ours, static (a, b) => (double)(a - b) * (a - b)).Average();
    return mse == 0 ? 99 : 10 * Math.Log10(255.0 * 255.0 / mse);
  }
}
