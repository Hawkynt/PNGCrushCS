using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using FileFormat.Core;
using FileFormat.Mp4;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.ProRes.Tests;

/// <summary>External interoperability checks for every shape the ProRes writer can produce.</summary>
[TestFixture]
public sealed class ProResVideoEncoderOracleTests {

  [TestCase("apcn", "yuv422p10le")]
  [TestCase("ap4h", "yuv444p12le")]
  [TestCase("ap4x", "yuv444p12le")]
  [Category("Oracle")]
  public void FFmpegMatchesProgressiveColourPlanesAtTheCodedDepth(string codec, string pixelFormat) {
    FFmpegOracle.RequireAvailable();

    const int WIDTH = 64;
    const int HEIGHT = 48;
    var stream = _Stream(WIDTH, HEIGHT, codec, bitsPerPixel: 24);
    var picture = codec == "apcn" ? _Pattern422(WIDTH, HEIGHT) : _Pattern444(WIDTH, HEIGHT);

    var (planes, decoded) = _EncodeAndDecodeWithFfmpeg(stream, picture, pixelFormat);
    _AssertColourPlanes(decoded, planes, WIDTH, HEIGHT, codec == "apcn" ? 1 : 0, maximumDelta: 1);
  }

  [TestCase(0x0201)]
  [TestCase(0x0206)]
  [Category("Oracle")]
  public void FFmpegMatchesBothInterlacedFieldOrdersAtTheCodedDepth(int fieldCode) {
    FFmpegOracle.RequireAvailable();

    // Fifty lines make each field twenty-five lines high, so the last macroblock row of each field
    // is partial. That reaches field mapping, the interlaced coefficient scan and bottom padding in
    // one external comparison rather than merely proving an ordinary even-macroblock field works.
    const int WIDTH = 64;
    const int HEIGHT = 50;
    var stream = _InterlacedStream(WIDTH, HEIGHT, "apcn", (ushort)fieldCode);

    var (planes, decoded) = _EncodeAndDecodeWithFfmpeg(stream, _Pattern422(WIDTH, HEIGHT), "yuv422p10le");
    _AssertColourPlanes(decoded, planes, WIDTH, HEIGHT, chromaShift: 1, maximumDelta: 1);
  }

  [Test]
  [Category("Oracle")]
  public void FFmpegMatchesEightBitAlphaFrom4444Exactly() {
    FFmpegOracle.RequireAvailable();

    const int WIDTH = 64;
    const int HEIGHT = 48;
    var stream = _Stream(WIDTH, HEIGHT, "ap4h", bitsPerPixel: 32);
    var picture = _Rgba32WithBinaryAlpha(WIDTH, HEIGHT);

    var (planes, decoded) = _EncodeAndDecodeWithFfmpeg(stream, picture, "yuva444p12le");
    _AssertColourPlanes(decoded, planes, WIDTH, HEIGHT, chromaShift: 0, maximumDelta: 1);
    _AssertAlpha(decoded, planes, WIDTH, HEIGHT, alphaBitDepth: 8);
  }

  [Test]
  [Category("Oracle")]
  public void FFmpegMatchesSixteenBitAlphaFrom4444XqExactly() {
    FFmpegOracle.RequireAvailable();

    const int WIDTH = 64;
    const int HEIGHT = 48;
    var stream = _Stream(WIDTH, HEIGHT, "ap4x", bitsPerPixel: 32);
    var picture = _Rgba64WithBinaryAlpha(WIDTH, HEIGHT);

    var (planes, decoded) = _EncodeAndDecodeWithFfmpeg(stream, picture, "yuva444p12le");
    _AssertColourPlanes(decoded, planes, WIDTH, HEIGHT, chromaShift: 0, maximumDelta: 1);
    _AssertAlpha(decoded, planes, WIDTH, HEIGHT, alphaBitDepth: 16);
  }

  private static (ProResPlanes Planes, byte[] Decoded) _EncodeAndDecodeWithFfmpeg(
    MediaStreamInfo stream,
    RawImage picture,
    string pixelFormat) {
    var encoder = ProResVideoEncoder.Create(stream);
    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);
    var described = encoder.DescribeStream();
    var planes = ProResVideoDecoder.Create(described).DecodePlanes(packet.Data, out _);

    var directory = Directory.CreateTempSubdirectory("prores-oracle");
    try {
      var movie = Path.Combine(directory.FullName, "frame.mov");
      var raw = Path.Combine(directory.FullName, "decoded.raw");
      File.WriteAllBytes(movie, VideoIO.Mux<Mp4Writer>([described], [packet]));

      var startInfo = new ProcessStartInfo(FFmpegOracle.ExecutablePath!) {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
      };
      foreach (var argument in new[] {
        "-hide_banner", "-loglevel", "error", "-y", "-threads", "1", "-i", movie,
        "-map", "0:v:0", "-frames:v", "1", "-vsync", "0",
        "-f", "rawvideo", "-pix_fmt", pixelFormat, raw,
      })
        startInfo.ArgumentList.Add(argument);

      using var process = Process.Start(startInfo);
      Assert.That(process, Is.Not.Null, "ffmpeg would not start");
      var stdout = process!.StandardOutput.ReadToEndAsync();
      var stderr = process.StandardError.ReadToEndAsync();
      if (!process.WaitForExit(60_000)) {
        try { process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        Assert.Fail("ffmpeg timed out while decoding the generated ProRes frame");
      }

      var diagnostics = string.Concat(stdout.Result, stderr.Result).Trim();
      Assert.Multiple(() => {
        Assert.That(process.ExitCode, Is.Zero, $"ffmpeg refused the generated ProRes frame: {diagnostics}");
        Assert.That(diagnostics, Is.Empty, "ffmpeg decoded the frame only after reporting an error");
        Assert.That(File.Exists(raw), Is.True, "ffmpeg produced no raw component planes");
      });

      return (planes, File.ReadAllBytes(raw));
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }

  private static void _AssertColourPlanes(
    byte[] decoded,
    ProResPlanes expected,
    int width,
    int height,
    int chromaShift,
    int maximumDelta) {
    var chromaWidth = (width + (1 << chromaShift) - 1) >> chromaShift;
    var ySamples = checked(width * height);
    var chromaSamples = checked(chromaWidth * height);
    var required = checked((ySamples + chromaSamples * 2) * 2);
    Assert.That(decoded.Length, Is.GreaterThanOrEqualTo(required));

    _AssertPlane(decoded, 0, expected.Luma, expected.Width, width, height, maximumDelta, "Y");
    _AssertPlane(decoded, ySamples * 2, expected.Cb, expected.ChromaWidth, chromaWidth, height, maximumDelta, "Cb");
    _AssertPlane(decoded, (ySamples + chromaSamples) * 2, expected.Cr, expected.ChromaWidth, chromaWidth, height, maximumDelta, "Cr");
  }

  private static void _AssertPlane(
    byte[] decoded,
    int decodedOffset,
    ushort[] expected,
    int expectedStride,
    int width,
    int height,
    int maximumDelta,
    string component) {
    var differing = 0;
    var worst = 0;

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var actual = BinaryPrimitives.ReadUInt16LittleEndian(decoded.AsSpan(decodedOffset + (y * width + x) * 2));
        var wanted = expected[y * expectedStride + x];
        var delta = Math.Abs(actual - wanted);
        if (delta != 0)
          ++differing;
        worst = Math.Max(worst, delta);
      }

    Assert.That(worst, Is.LessThanOrEqualTo(maximumDelta),
      $"FFmpeg's {component} plane differs in {differing} samples; worst delta {worst}");
  }

  private static void _AssertAlpha(byte[] decoded, ProResPlanes expected, int width, int height, int alphaBitDepth) {
    Assert.That(expected.Alpha, Is.Not.Null);
    var planeSamples = checked(width * height);
    var alphaOffset = checked(planeSamples * 3 * 2);
    Assert.That(decoded.Length, Is.EqualTo(alphaOffset + planeSamples * 2),
      "yuva444p12le should contain four equally sized twelve-bit planes");

    var differing = 0;
    var worst = 0;
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var source = expected.Alpha![y * expected.Width + x];
        var wanted = alphaBitDepth == 8
          ? (source << 4) | (source >> 4)
          : source >> 4;
        var actual = BinaryPrimitives.ReadUInt16LittleEndian(decoded.AsSpan(alphaOffset + (y * width + x) * 2));
        var delta = Math.Abs(actual - wanted);
        if (delta != 0)
          ++differing;
        worst = Math.Max(worst, delta);
      }

    Assert.That(worst, Is.Zero,
      $"FFmpeg's alpha plane differs in {differing} samples; worst delta {worst}");
  }

  private static MediaStreamInfo _Stream(int width, int height, string codec, int bitsPerPixel) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(codec),
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
  };

  private static MediaStreamInfo _InterlacedStream(int width, int height, string codec, ushort fieldCode) {
    var entry = new byte[96];
    BinaryPrimitives.WriteUInt32BigEndian(entry, (uint)entry.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(4), CodecTag.FromCharacters(codec).Value);
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(14), 1);
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(32), (ushort)width);
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(34), (ushort)height);
    BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(36), 0x00480000);
    BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(40), 0x00480000);
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(48), 1);
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(82), 24);
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(84), ushort.MaxValue);
    BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(86), 10);
    "fiel"u8.CopyTo(entry.AsSpan(90));
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(94), fieldCode);

    return new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters(codec),
      TimeBase = new Rational(1, 25),
      FrameRate = new Rational(25, 1),
      Width = width,
      Height = height,
      BitsPerPixel = 24,
      CodecPrivateData = entry,
    };
  }

  private static RawImage _Pattern422(int width, int height) {
    var chromaWidth = (width + 1) / 2;
    var y = new ushort[width * height];
    var cb = new ushort[chromaWidth * height];
    var cr = new ushort[chromaWidth * height];
    for (var row = 0; row < height; ++row) {
      for (var x = 0; x < width; ++x)
        y[row * width + x] = (ushort)(160 + (x * 7 + row * 13) % 700);
      for (var x = 0; x < chromaWidth; ++x) {
        cb[row * chromaWidth + x] = (ushort)(240 + (x * 11 + row * 5) % 540);
        cr[row * chromaWidth + x] = (ushort)(780 - (x * 3 + row * 7) % 540);
      }
    }
    return _Planar(width, height, PixelFormat.Yuv422P10, y, cb, cr);
  }

  private static RawImage _Pattern444(int width, int height) {
    var y = new ushort[width * height];
    var cb = new ushort[width * height];
    var cr = new ushort[width * height];
    for (var row = 0; row < height; ++row)
      for (var x = 0; x < width; ++x) {
        var at = row * width + x;
        y[at] = (ushort)(640 + (x * 23 + row * 31) % 2700);
        cb[at] = (ushort)(800 + (x * 17 + row * 19) % 2300);
        cr[at] = (ushort)(3200 - (x * 13 + row * 11) % 2300);
      }
    return _Planar(width, height, PixelFormat.Yuv444P12, y, cb, cr);
  }

  private static RawImage _Rgba32WithBinaryAlpha(int width, int height) {
    var bytes = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 4;
        bytes[at] = (byte)(40 + (x * 3 + y) % 180);
        bytes[at + 1] = (byte)(50 + (x + y * 5) % 170);
        bytes[at + 2] = (byte)(60 + (x * 7 + y * 3) % 160);
        bytes[at + 3] = (byte)(((x / 8 + y / 8) & 1) == 0 ? 0 : 255);
      }
    return new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = bytes };
  }

  private static RawImage _Rgba64WithBinaryAlpha(int width, int height) {
    var bytes = new byte[width * height * 8];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 8;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at), (ushort)(10000 + (x * 733 + y * 191) % 40000));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at + 2), (ushort)(12000 + (x * 293 + y * 857) % 38000));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at + 4), (ushort)(14000 + (x * 571 + y * 353) % 36000));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at + 6), ((x / 8 + y / 8) & 1) == 0 ? (ushort)0 : ushort.MaxValue);
      }
    return new() { Width = width, Height = height, Format = PixelFormat.Rgba64, PixelData = bytes };
  }

  private static RawImage _Planar(
    int width,
    int height,
    PixelFormat format,
    ushort[] y,
    ushort[] cb,
    ushort[] cr) {
    var bytes = new byte[checked((y.Length + cb.Length + cr.Length) * 2)];
    var at = 0;
    foreach (var plane in new[] { y, cb, cr })
      foreach (var sample in plane) {
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), sample);
        at += 2;
      }
    return new() { Width = width, Height = height, Format = format, PixelData = bytes };
  }
}
