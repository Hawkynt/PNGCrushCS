using System;
using System.Diagnostics;
using System.IO;
using FileFormat.Avi;
using FileFormat.Codecs.Hap;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// Holds what the Hap encoder writes to FFmpeg's independent Hap decoder, sample for sample.
/// </summary>
/// <remarks>
/// A frame that goes out through this encoder and comes back through this decoder proves only that
/// the two agree, and two halves written together agree about their shared mistakes. FFmpeg's Hap
/// decoder shares no code with either half, so what it reads back is the first opinion from outside.
/// <para/>
/// FFmpeg reaches four of the seven Hap textures: <c>Hap1</c>, <c>Hap5</c>, <c>HapY</c> and
/// <c>HapA</c>. It refuses the <c>0x0D</c> multi-image frame that carries Hap Q Alpha, and the AVI
/// FourCCs for Hap R and Hap HDR reach no decoder it has. <c>Hap7</c> and <c>HapH</c> are therefore
/// held to Vidvox's own <c>hap.c</c> and to bcdec instead, in <see cref="HapReferenceOracle"/> and
/// the workflow that builds it. Hap Q Alpha is covered here all the same: its two nested images are
/// complete Hap image sections, so lifting them out gives a standalone Hap Q frame and a standalone
/// Hap Alpha-Only frame that FFmpeg does read, which leaves only the four-byte wrapper around them
/// untested by an outside reader — and that the structural tests already pin.
/// <para/>
/// Every frame here is written in four chunks, so the decode-instructions section, the per-chunk
/// compressor table and the Snappy payloads are all on the path being checked rather than the
/// single-chunk shape that a small fixture would otherwise take.
/// </remarks>
[TestFixture]
[Category("Conformance")]
public sealed class HapFfmpegOracleTests {

  private const int _WIDTH = 64;
  private const int _HEIGHT = 48;
  private const int _CHUNKS = 4;

  /// <summary>A picture with a gradient in every channel, so a dropped channel cannot pass.</summary>
  private static byte[] _Source() {
    var pixels = new byte[_WIDTH * _HEIGHT * 4];
    for (var y = 0; y < _HEIGHT; ++y)
      for (var x = 0; x < _WIDTH; ++x) {
        var at = (y * _WIDTH + x) * 4;
        pixels[at] = (byte)(x * 4);
        pixels[at + 1] = (byte)(y * 5);
        pixels[at + 2] = (byte)(255 - x * 3);
        pixels[at + 3] = (byte)(40 + ((x + y) * 3 & 0xBF));
      }

    return pixels;
  }

  private static MediaStreamInfo _Stream(string code) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(code),
    Handler = CodecTag.FromCharacters(code),
    Width = _WIDTH,
    Height = _HEIGHT,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  [TestCase("Hap1", "rgb24", 3, TestName = "{m}(Hap, DXT1)")]
  [TestCase("Hap5", "rgba", 4, TestName = "{m}(Hap Alpha, DXT5)")]
  [TestCase("HapY", "rgb24", 3, TestName = "{m}(Hap Q, scaled YCoCg DXT5)")]
  [TestCase("HapA", "gray", 1, TestName = "{m}(Hap Alpha-Only, RGTC1)")]
  public void FfmpegReadsBackTheSameSamplesTheHapDecoderDoes(string code, string pixelFormat, int channels) {
    FFmpegOracle.RequireAvailable();

    var encoder = HapVideoEncoder.Create(_Stream(code), _CHUNKS);
    var source = new RawImage {
      Width = _WIDTH, Height = _HEIGHT, Format = PixelFormat.Rgba32, PixelData = _Source(),
    };
    Assert.That(encoder.TryEncode(source, 0, out var packet), Is.True);

    var decoder = HapDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var ours), Is.True);

    var (theirs, diagnostics) = _Decode(packet.Data.ToArray(), code, pixelFormat);
    var expected = _WIDTH * _HEIGHT * channels;

    Assert.Multiple(() => {
      Assert.That(theirs.Length, Is.EqualTo(expected), $"ffmpeg decoded {theirs.Length} bytes: {diagnostics}");
      Assert.That(ours.PixelData.Length, Is.EqualTo(expected), $"our decoder produced {ours.Format}");
    });

    // Both sides decode the same block format by the same published rules, so this is exact and not
    // a tolerance: a difference of one anywhere means one of the two is reading the texture wrong.
    for (var i = 0; i < expected; ++i)
      if (ours.PixelData[i] != theirs[i])
        Assert.Fail(
          $"{code} sample {i}: this decoder says {ours.PixelData[i]} and ffmpeg says {theirs[i]}");
  }

  [Test]
  public void FfmpegReadsBothImagesNestedInAHapQAlphaFrame() {
    FFmpegOracle.RequireAvailable();

    var encoder = HapVideoEncoder.Create(_Stream("HapM"), _CHUNKS);
    var pixels = _Source();
    var source = new RawImage {
      Width = _WIDTH, Height = _HEIGHT, Format = PixelFormat.Rgba32, PixelData = pixels,
    };
    Assert.That(encoder.TryEncode(source, 0, out var packet), Is.True);

    var frame = packet.Data.ToArray();
    var top = HapSection.ReadAt(frame, 0, "Hap Q Alpha frame");
    var payload = frame.AsSpan(top.DataOffset, top.DataLength);
    var colour = HapSection.ReadAt(payload, 0, "Hap Q Alpha colour image");
    var alpha = HapSection.ReadAt(payload, colour.EndOffset, "Hap Q Alpha alpha image");

    Assert.Multiple(() => {
      Assert.That(top.Type, Is.EqualTo(0x0D));
      Assert.That(colour.Type & 0x0F, Is.EqualTo(0x0F), "the colour image is a scaled YCoCg DXT5 texture");
      Assert.That(alpha.Type & 0x0F, Is.EqualTo(0x01), "the alpha image is an RGTC1 texture");
    });

    // Each nested image carries its own section header, so the bytes from its header to its end are
    // already a whole Hap frame of that one texture. Handing them to ffmpeg under the FourCC that
    // names that texture asks an outside reader about the payloads the combined frame is made of.
    var colourFrame = payload[..colour.EndOffset].ToArray();
    var alphaFrame = payload.Slice(colour.EndOffset, alpha.EndOffset - colour.EndOffset).ToArray();

    var (colourSamples, colourDiagnostics) = _Decode(colourFrame, "HapY", "rgb24");
    var (alphaSamples, alphaDiagnostics) = _Decode(alphaFrame, "HapA", "gray");

    Assert.Multiple(() => {
      Assert.That(colourSamples.Length, Is.EqualTo(_WIDTH * _HEIGHT * 3), colourDiagnostics);
      Assert.That(alphaSamples.Length, Is.EqualTo(_WIDTH * _HEIGHT), alphaDiagnostics);
    });

    var decoder = HapDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var ours), Is.True);
    Assert.That(ours.PixelData.Length, Is.EqualTo(_WIDTH * _HEIGHT * 4));

    // What the combined decode produced has to be what ffmpeg read out of the two images separately,
    // which is what says the two were combined the way the format states rather than merely carried.
    for (var pixel = 0; pixel < _WIDTH * _HEIGHT; ++pixel) {
      for (var channel = 0; channel < 3; ++channel)
        if (ours.PixelData[pixel * 4 + channel] != colourSamples[pixel * 3 + channel])
          Assert.Fail(
            $"Hap Q Alpha pixel {pixel} channel {channel}: the combined decode says "
            + $"{ours.PixelData[pixel * 4 + channel]} and ffmpeg's read of the nested colour image says "
            + $"{colourSamples[pixel * 3 + channel]}");

      if (ours.PixelData[pixel * 4 + 3] != alphaSamples[pixel])
        Assert.Fail(
          $"Hap Q Alpha pixel {pixel} alpha: the combined decode says {ours.PixelData[pixel * 4 + 3]} "
          + $"and ffmpeg's read of the nested alpha image says {alphaSamples[pixel]}");
    }
  }

  /// <summary>Wraps one Hap frame in an AVI under the given FourCC and decodes it with ffmpeg.</summary>
  private static (byte[] Samples, string Diagnostics) _Decode(byte[] frame, string code, string pixelFormat) {
    var described = HapVideoEncoder.Create(_Stream(code), 1).DescribeStream();
    var packet = new CodedPacket {
      StreamIndex = 0,
      Data = frame,
      PresentationTimestamp = 0,
      DecodeTimestamp = 0,
      IsKeyFrame = true,
    };

    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");
    var raw = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".bin");

    try {
      File.WriteAllBytes(path, VideoIO.Mux<AviWriter>([described], [packet]));

      var startInfo = new ProcessStartInfo(FFmpegOracle.ExecutablePath!) {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
      };
      foreach (var argument in new[] {
        "-hide_banner", "-loglevel", "error", "-y", "-i", path,
        "-map", "0:v:0", "-an", "-sn", "-dn", "-vsync", "0",
        "-f", "rawvideo", "-pix_fmt", pixelFormat, raw,
      })
        startInfo.ArgumentList.Add(argument);

      using var process = Process.Start(startInfo);
      if (process == null)
        return ([], "ffmpeg would not start");

      var diagnostics = process.StandardError.ReadToEnd().Trim();
      process.WaitForExit();

      return (File.Exists(raw) ? File.ReadAllBytes(raw) : [], diagnostics);
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
      try { File.Delete(raw); } catch { /* best effort */ }
    }
  }
}
