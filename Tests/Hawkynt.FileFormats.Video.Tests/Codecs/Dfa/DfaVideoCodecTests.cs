using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FileFormat.Codecs.Dfa;
using FileFormat.Core;
using FileFormat.Dfa;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>Chronomaster DFA: the container both ways, the codec both ways, and FFmpeg as the oracle for each.</summary>
[TestFixture]
public sealed class DfaVideoCodecTests {

  [Test]
  [Category("Unit")]
  public void DfiaIsDecodedAndEncoded() {
    var stream = _Stream(64, 48);
    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<DfaVideoDecoder>());
      Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<DfaVideoEncoder>());
    });
  }

  [TestCase(96, 64)]
  [TestCase(63, 47)]
  [Category("Unit")]
  public void APictureInADfaPaletteComesBackExactly(int width, int height) {
    var (_, ours, originals) = _Encode(width, height, 26);
    for (var i = 0; i < ours.Count; ++i)
      Assert.That(ours[i], Is.EqualTo(originals[i]), $"picture {i}");
  }

  [Test]
  [Category("Unit")]
  public void ThePaletteKeepsSixBitsAComponentAsTheDecoderExpandsThem() {
    for (var component = 0; component < 256; ++component) {
      var expanded = DfaEncoder.Expand(DfaEncoder.Quantise(component));
      Assert.That(Math.Abs(expanded - component), Is.LessThanOrEqualTo(2), $"component {component}");
    }
  }

  [Test]
  [Category("Unit")]
  public void TheContainerRoundTripsItsFrames() {
    var (bytes, ours, _) = _Encode(96, 64, 5);
    var container = DfaContainer.FromBytes(bytes);
    var stream = DfaContainer.Streams(container)[0];
    var packets = DfaContainer.ReadPackets(container).ToList();
    Assert.Multiple(() => {
      Assert.That(DfaContainer.MatchesSignature(bytes), Is.True);
      Assert.That((stream.Width, stream.Height), Is.EqualTo((96, 64)));
      Assert.That(stream.FrameRate, Is.EqualTo(new Rational(1000, 67)));
      Assert.That(stream.DeclaredFrameCount, Is.EqualTo(5));
      Assert.That(packets, Has.Count.EqualTo(ours.Count));
    });
  }

  [Test]
  [Category("Unit")]
  public void ADamagedChunkIsRefusedByName() {
    var decoder = DfaVideoDecoder.Create(_Stream(16, 16));
    // A TSW1 chunk whose starting offset lies past the picture.
    byte[] packet = [(byte)'T', (byte)'S', (byte)'W', (byte)'1', 8, 0, 0, 0, 3, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0];
    Assert.That(() => decoder.TryDecode(new(0, packet), out _), Throws.InstanceOf<InvalidDataException>().With.Message.Contains("TSW1"));
  }

  [TestCase(96, 64)]
  [TestCase(63, 47)]
  [Category("Conformance")]
  public void FFmpegReadsWhatTheContainerAndEncoderWriteAsThisDecoderDoes(int width, int height) {
    FFmpegOracle.RequireAvailable();
    var (bytes, ours, _) = _Encode(width, height, 26);
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".dfa");
    try {
      File.WriteAllBytes(path, bytes);
      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(path, "rgb24");
      Assert.That(decoded, Is.True, detail);
      var size = width * height * 3;
      Assert.That(samples.Length / size, Is.EqualTo(ours.Count), "picture count");
      for (var i = 0; i < ours.Count; ++i)
        Assert.That(samples.AsSpan(i * size, size).ToArray(), Is.EqualTo(_Rgb(ours[i], width, height)), $"picture {i}");
    } finally {
      File.Delete(path);
    }
  }

  /// <summary>
  /// Opt-in: point <c>DFA_SAMPLES</c> at the films of <c>samples.ffmpeg.org/game-formats/chronomaster-dfa</c>.
  /// </summary>
  /// <remarks>
  /// Every picture FFmpeg draws must be this decoder's. FFmpeg refuses every DDS1 frame, so those are held
  /// instead to the whole film's RGB as drawn by FFmpeg n9.1's own <c>libavcodec/dfa.c</c> chunk decoders,
  /// compiled on their own with the one DDS1 comparison turned the right way round; the same build left as
  /// it is reproduces FFmpeg's output for all four films byte for byte. Its SHA-256 for each film is below.
  /// </remarks>
  [Test]
  [Category("Conformance")]
  [Explicit("Needs the Chronomaster DFA films; point DFA_SAMPLES at them.")]
  public void EveryPictureOfARealDfaFilmMatchesFFmpeg() {
    FFmpegOracle.RequireAvailable();
    var directory = Environment.GetEnvironmentVariable("DFA_SAMPLES");
    if (string.IsNullOrWhiteSpace(directory))
      Assert.Inconclusive("Set DFA_SAMPLES to the directory of Chronomaster DFA films.");
    var corrected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
      ["0000.dfa"] = "9c82eae6d401820cb391cc26e1a0d494d19c4f78e8e062e4ff64b3d2e6c8e92a",
      ["0001.dfa"] = "c82f7755174f5111e5bfeded2fb1643bb11cc59ad1b795b008694b786abf39fd",
      ["0002.dfa"] = "79158f677b7a49dd5bb5e5665f170c44b96c730ea295c4ab69d273571f6ee2e3",
      ["LOGOS.DFA"] = "34177fb4ceef30d2063cbe0f8836987e6d614bd553ac77fd6070719a05ec9cfb",
    };

    var total = 0;
    foreach (var film in Directory.GetFiles(directory!).Where(static f => f.EndsWith(".dfa", StringComparison.OrdinalIgnoreCase))) {
      var name = Path.GetFileName(film);
      var container = DfaContainer.FromBytes(File.ReadAllBytes(film));
      var stream = DfaContainer.Streams(container)[0];
      var decoder = DfaVideoDecoder.Create(stream);
      var ours = new List<byte[]>();
      var refusedByFFmpeg = new List<bool>();
      foreach (var packet in DfaContainer.ReadPackets(container)) {
        decoder.TryDecode(packet, out var picture);
        ours.Add(picture.ToRgb24());
        refusedByFFmpeg.Add(_HasChunk(packet.Data.Span, DfaChunk.Dds1));
      }

      var (decoded, detail, samples) = FFmpegOracle.TryDecodeUnscaled(film, "rgb24", tolerateDiagnostics: true);
      Assert.That(decoded, Is.True, detail);
      var size = stream.Width * stream.Height * 3;
      var theirs = 0;
      for (var i = 0; i < ours.Count && !refusedByFFmpeg[i]; ++i, ++theirs)
        Assert.That(samples.AsSpan(theirs * size, size).SequenceEqual(ours[i]), Is.True, $"{name} picture {i}");
      Assert.That(theirs, Is.EqualTo(samples.Length / size).Or.LessThan(ours.Count), $"{name}: FFmpeg drew pictures this test did not compare");

      var hash = Convert.ToHexStringLower(SHA256.HashData(ours.SelectMany(static p => p).ToArray()));
      if (corrected.TryGetValue(name, out var expected))
        Assert.That(hash, Is.EqualTo(expected), $"{name}: the whole film against the corrected FFmpeg chunk decoders");
      TestContext.Out.WriteLine($"{name}: {theirs} of {ours.Count} pictures identical to ffmpeg; all {ours.Count} to the corrected decoders");
      total += ours.Count;
    }
    Assert.That(total, Is.GreaterThan(0));
  }

  // ==========================================================================================

  private static bool _HasChunk(ReadOnlySpan<byte> packet, int type) {
    for (var at = 0; at + 12 <= packet.Length;) {
      if (BitConverter.ToInt32(packet[(at + 8)..]) == type)
        return true;
      at += 12 + BitConverter.ToInt32(packet[(at + 4)..]);
    }
    return false;
  }

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("DFIA"),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 15),
    FrameRate = new Rational(15, 1),
  };

  /// <summary>A film of moving and still regions in a palette DFA can hold, a black picture among them.</summary>
  private static (byte[] Film, List<byte[]> Ours, List<byte[]> Originals) _Encode(int width, int height, int count) {
    var encoder = DfaVideoEncoder.Create(_Stream(width, height));
    var stream = encoder.DescribeStream();
    var decoder = DfaVideoDecoder.Create(stream);
    var palette = new byte[256 * 3];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)DfaEncoder.Expand(i & 63);
      palette[i * 3 + 1] = (byte)DfaEncoder.Expand((i * 5) & 63);
      palette[i * 3 + 2] = (byte)DfaEncoder.Expand((i >> 2) & 63);
    }
    var packets = new List<CodedPacket>();
    var ours = new List<byte[]>();
    var originals = new List<byte[]>();
    for (var n = 0; n < count; ++n) {
      var pixels = new byte[width * height];
      if (n != 7)
        for (var y = 0; y < height; ++y)
          for (var x = 0; x < width; ++x)
            pixels[y * width + x] = (byte)(x < width / 2 ? (x + y * 3 + n) & 0x3F : ((x / 6) ^ (y / 4)) * 9);
      var picture = new RawImage { Width = width, Height = height, Format = PixelFormat.Indexed8, PixelData = pixels, Palette = palette, PaletteCount = 256 };
      encoder.TryEncode(picture, n, out var packet);
      packets.Add(packet);
      decoder.TryDecode(packet, out var decoded);
      ours.Add(decoded.PixelData.ToArray());
      originals.Add(pixels);
    }
    return (VideoIO.Mux<DfaWriter>([stream], packets), ours, originals);
  }

  private static byte[] _Rgb(byte[] indices, int width, int height)
    => new RawImage { Width = width, Height = height, Format = PixelFormat.Indexed8, PixelData = indices, Palette = _PaletteOfTest(), PaletteCount = 256 }.ToRgb24();

  private static byte[] _PaletteOfTest() {
    var palette = new byte[256 * 3];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)DfaEncoder.Expand(i & 63);
      palette[i * 3 + 1] = (byte)DfaEncoder.Expand((i * 5) & 63);
      palette[i * 3 + 2] = (byte)DfaEncoder.Expand((i >> 2) & 63);
    }
    return palette;
  }
}
