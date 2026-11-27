using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.DeluxePaintAnm;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>Deluxe Paint Animation: the container both ways, the codec both ways, and FFmpeg as the oracle for each.</summary>
[TestFixture]
public sealed class DeluxePaintAnmVideoCodecTests {

  [Test]
  [Category("Unit")]
  public void LpfIsDecodedAndEncoded() {
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(64, 48));
    Assert.Multiple(() => {
      Assert.That(encoder, Is.InstanceOf<DeluxePaintAnmVideoEncoder>());
      Assert.That(VideoFormatRegistry.CreateDecoder(encoder.DescribeStream()), Is.InstanceOf<DeluxePaintAnmVideoDecoder>());
    });
  }

  [TestCase(320, 200)]
  [TestCase(63, 47)]
  [Category("Unit")]
  public void APictureInTheFilmsPaletteComesBackExactly(int width, int height) {
    var (_, ours, originals, _) = _Encode(width, height, 20);
    for (var i = 0; i < ours.Count; ++i)
      Assert.That(ours[i], Is.EqualTo(originals[i]), $"picture {i}");
  }

  [Test]
  [Category("Unit")]
  public void AnEmptyRecordYieldsNoPicture() {
    var decoder = DeluxePaintAnmVideoDecoder.Create(DeluxePaintAnmVideoEncoder.Create(_Stream(16, 16)).DescribeStream());
    Assert.That(decoder.TryDecode(new(0, Array.Empty<byte>()), out _), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheContainerPacksRecordsIntoPagesAndReadsThemBack() {
    // Big records force several pages.
    var (bytes, ours, _, packets) = _Encode(320, 200, 12, noisy: true);
    var container = DeluxePaintAnmContainer.FromBytes(bytes);
    var stream = DeluxePaintAnmContainer.Streams(container)[0];
    var read = DeluxePaintAnmContainer.ReadPackets(container).ToList();
    Assert.Multiple(() => {
      Assert.That(DeluxePaintAnmContainer.MatchesSignature(bytes), Is.True);
      Assert.That((stream.Width, stream.Height), Is.EqualTo((320, 200)));
      Assert.That(stream.FrameRate, Is.EqualTo(new Rational(15, 1)));
      Assert.That(BitConverter.ToUInt16(bytes, 6), Is.GreaterThan(1), "pages");
      Assert.That(read.Select(static p => p.Data.ToArray()), Is.EqualTo(packets.Select(static p => p.Data.ToArray())));
    });
  }

  [Test]
  [Category("Unit")]
  public void APictureTooLargeForAPageIsRefused()
    => Assert.That(() => DeluxePaintAnmVideoEncoder.Create(_Stream(640, 480)), Throws.InstanceOf<NotSupportedException>());

  [TestCase(320, 200, false)]
  [TestCase(320, 200, true)]
  [TestCase(63, 47, false)]
  [Category("Conformance")]
  public void FFmpegReadsWhatTheContainerAndEncoderWriteAsThisDecoderDoes(int width, int height, bool noisy) {
    FFmpegOracle.RequireAvailable();
    var (bytes, ours, _, _) = _Encode(width, height, 12, noisy);
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".anm");
    try {
      File.WriteAllBytes(path, bytes);
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, "rgb24");
      Assert.That(decoded, Is.True, detail);
      var size = width * height * 3;
      Assert.That(samples.Length / size, Is.EqualTo(ours.Count), "picture count");
      var palette = _Palette();
      for (var i = 0; i < ours.Count; ++i)
        Assert.That(samples.AsSpan(i * size, size).ToArray(), Is.EqualTo(_Rgb(ours[i], width, height, palette)), $"picture {i}");
    } finally {
      File.Delete(path);
    }
  }

  /// <summary>Opt-in: point <c>ANM_SAMPLES</c> at the films of <c>samples.ffmpeg.org/game-formats/anm</c>.</summary>
  [Test]
  [Category("Conformance")]
  [Explicit("Needs real Deluxe Paint Animation films; point ANM_SAMPLES at them.")]
  public void EveryPictureOfARealFilmMatchesFFmpeg() {
    FFmpegOracle.RequireAvailable();
    var directory = Environment.GetEnvironmentVariable("ANM_SAMPLES");
    if (string.IsNullOrWhiteSpace(directory))
      Assert.Inconclusive("Set ANM_SAMPLES to a directory of Deluxe Paint Animation films.");
    var total = 0;
    foreach (var film in Directory.GetFiles(directory!).Where(static f => f.EndsWith(".anm", StringComparison.OrdinalIgnoreCase))) {
      var name = Path.GetFileName(film);
      var container = DeluxePaintAnmContainer.FromBytes(File.ReadAllBytes(film));
      var stream = DeluxePaintAnmContainer.Streams(container)[0];
      var decoder = DeluxePaintAnmVideoDecoder.Create(stream);
      var ours = new List<byte[]>();
      foreach (var packet in DeluxePaintAnmContainer.ReadPackets(container))
        if (decoder.TryDecode(packet, out var picture))
          ours.Add(picture.ToRgb24());

      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(film, "rgb24", tolerateDiagnostics: true);
      Assert.That(decoded, Is.True, detail);
      var size = stream.Width * stream.Height * 3;
      Assert.That(samples.Length / size, Is.EqualTo(ours.Count), $"{name}: picture count");
      for (var i = 0; i < ours.Count; ++i)
        Assert.That(samples.AsSpan(i * size, size).SequenceEqual(ours[i]), Is.True, $"{name} picture {i}");
      TestContext.Out.WriteLine($"{name}: {ours.Count} pictures identical to ffmpeg");
      total += ours.Count;
    }
    Assert.That(total, Is.GreaterThan(0));
  }

  // ==========================================================================================

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("LPF "),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 15),
    FrameRate = new Rational(15, 1),
  };

  private static byte[] _Palette() {
    var palette = new byte[256 * 3];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)(i * 7);
      palette[i * 3 + 2] = (byte)(255 - i);
    }
    return palette;
  }

  /// <summary>A film of moving and still regions in one palette, long runs and a picture that does not change among them.</summary>
  private static (byte[] Film, List<byte[]> Ours, List<byte[]> Originals, List<CodedPacket> Packets) _Encode(int width, int height, int count, bool noisy = false) {
    var encoder = DeluxePaintAnmVideoEncoder.Create(_Stream(width, height));
    var palette = _Palette();
    var random = new Random(5);
    var packets = new List<CodedPacket>();
    var originals = new List<byte[]>();
    for (var n = 0; n < count; ++n) {
      var pixels = new byte[width * height];
      var step = n == 6 ? 5 : n;
      for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x)
          pixels[y * width + x] = noisy ? (byte)random.Next(256)
            : (byte)(x < width / 3 ? (x + y + step) & 0xFF : y < height / 2 ? 17 : ((x / 5) ^ (y / 3)) * 3);
      encoder.TryEncode(new RawImage { Width = width, Height = height, Format = PixelFormat.Indexed8, PixelData = pixels, Palette = palette, PaletteCount = 256 }, n, out var packet);
      packets.Add(packet);
      originals.Add(pixels);
    }
    var stream = encoder.DescribeStream();
    var decoder = DeluxePaintAnmVideoDecoder.Create(stream);
    var ours = new List<byte[]>();
    foreach (var packet in packets) {
      Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
      ours.Add(decoded.PixelData.ToArray());
    }
    return (VideoIO.Mux<DeluxePaintAnmWriter>([stream], packets), ours, originals, packets);
  }

  private static byte[] _Rgb(byte[] indices, int width, int height, byte[] palette)
    => new RawImage { Width = width, Height = height, Format = PixelFormat.Indexed8, PixelData = indices, Palette = palette, PaletteCount = 256 }.ToRgb24();
}
