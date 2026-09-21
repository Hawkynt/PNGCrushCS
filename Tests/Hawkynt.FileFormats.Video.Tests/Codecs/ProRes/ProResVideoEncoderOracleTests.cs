using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.Mp4;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.ProRes.Tests;

/// <summary>
/// External interoperability checks for every shape the ProRes writer can produce.
/// </summary>
/// <remarks>
/// Each case writes one frame, muxes it into QuickTime and hands the file to FFmpeg. What is compared
/// is FFmpeg's component planes against this package's, at the depth the profile codes them at, which
/// is the last point before the two decoders' display conventions can differ. Every profile, both
/// field orders, both alpha depths and the absence of alpha each get their own case, because the
/// defect this guards against is a writer which handles the common profile and quietly degrades the
/// rest: a 4444 stream whose alpha is dropped or flattened still decodes, and still looks plausible.
/// </remarks>
[TestFixture]
public sealed class ProResVideoEncoderOracleTests {

  private const int _WIDTH = 64;
  private const int _HEIGHT = 48;

  /// <summary>
  /// All six profiles, progressive, at the sampling and depth each of them codes.
  /// </summary>
  /// <remarks>
  /// The per-profile source tolerance is the quantisation each profile's data rate buys over this
  /// picture, measured. It rises as the rate falls, which is the whole difference between the six
  /// names, and it is what makes the check a statement about the profile rather than a formality.
  /// </remarks>
  [TestCase("apco", "yuv422p10le", 40)]
  [TestCase("apcs", "yuv422p10le", 8)]
  [TestCase("apcn", "yuv422p10le", 6)]
  [TestCase("apch", "yuv422p10le", 4)]
  [TestCase("ap4h", "yuv444p12le", 16)]
  [TestCase("ap4x", "yuv444p12le", 16)]
  [Category("Oracle")]
  public void FFmpegMatchesProgressiveColourPlanesAtTheCodedDepth(
    string codec, string pixelFormat, int sourceDelta) {
    FFmpegOracle.RequireAvailable();

    var fourFourFour = pixelFormat.Contains("444", StringComparison.Ordinal);
    var stream = _Stream(_WIDTH, _HEIGHT, codec, bitsPerPixel: 24);
    var (picture, source) = _Smooth(_WIDTH, _HEIGHT, fourFourFour);

    var (planes, decoded) = _EncodeAndDecodeWithFfmpeg(stream, picture, pixelFormat);
    var chromaShift = fourFourFour ? 0 : 1;
    ProResPlaneComparison.AssertColour(decoded, planes, _WIDTH, _HEIGHT, chromaShift, maximumDelta: 1);
    ProResPlaneComparison.AssertColourResemblesSource(
      decoded, source, _WIDTH, _HEIGHT, chromaShift, sourceDelta);
  }

  /// <summary>
  /// Both field orders, in both samplings, at a height whose fields are an odd number of rows.
  /// </summary>
  /// <remarks>
  /// Fifty lines make each field twenty-five, so the last macroblock row of each field is partial.
  /// That reaches field mapping, the interlaced coefficient scan and bottom padding in one external
  /// comparison rather than merely proving an ordinary even-macroblock field works.
  /// </remarks>
  [TestCase("apcn", 0x0201, "yuv422p10le", 6)]
  [TestCase("apcn", 0x0206, "yuv422p10le", 6)]
  [TestCase("ap4h", 0x0201, "yuv444p12le", 16)]
  [TestCase("ap4h", 0x0206, "yuv444p12le", 16)]
  [Category("Oracle")]
  public void FFmpegMatchesBothInterlacedFieldOrdersAtTheCodedDepth(
    string codec, int fieldCode, string pixelFormat, int sourceDelta) {
    FFmpegOracle.RequireAvailable();

    const int HEIGHT = 50;
    var fourFourFour = pixelFormat.Contains("444", StringComparison.Ordinal);
    var stream = _InterlacedStream(_WIDTH, HEIGHT, codec, (ushort)fieldCode, bitsPerPixel: 24);
    var (picture, source) = _Smooth(_WIDTH, HEIGHT, fourFourFour);

    var (planes, decoded) = _EncodeAndDecodeWithFfmpeg(stream, picture, pixelFormat);
    var chromaShift = fourFourFour ? 0 : 1;
    ProResPlaneComparison.AssertColour(decoded, planes, _WIDTH, HEIGHT, chromaShift, maximumDelta: 1);
    ProResPlaneComparison.AssertColourResemblesSource(
      decoded, source, _WIDTH, HEIGHT, chromaShift, sourceDelta);
  }

  /// <summary>A matte that varies everywhere, at both alpha depths and in both 4:4:4 profiles.</summary>
  [TestCase("ap4h", 8)]
  [TestCase("ap4h", 16)]
  [TestCase("ap4x", 8)]
  [TestCase("ap4x", 16)]
  [Category("Oracle")]
  public void FFmpegMatchesAVaryingMatteExactly(string codec, int alphaBits) {
    FFmpegOracle.RequireAvailable();

    var stream = _Stream(_WIDTH, _HEIGHT, codec, bitsPerPixel: 32);
    var (picture, matte) = alphaBits == 8
      ? _Rgba32(_WIDTH, _HEIGHT, static (x, y) => (byte)((x * 5 + y * 3) & 0xFF))
      : _Rgba64(_WIDTH, _HEIGHT, static (x, y) => (ushort)((x * 1021 + y * 4093) & 0xFFFF));

    var (planes, decoded) = _EncodeAndDecodeWithFfmpeg(stream, picture, "yuva444p12le");
    ProResPlaneComparison.AssertColour(decoded, planes, _WIDTH, _HEIGHT, chromaShift: 0, maximumDelta: 1);
    ProResPlaneComparison.AssertAlphaIsTheSourceMatte(decoded, matte, _WIDTH, _HEIGHT, alphaBits);
  }

  /// <summary>A matte with only the two extremes in it, which is the one run coding compresses hardest.</summary>
  [TestCase("ap4h", 8)]
  [TestCase("ap4x", 16)]
  [Category("Oracle")]
  public void FFmpegMatchesABinaryMatteExactly(string codec, int alphaBits) {
    FFmpegOracle.RequireAvailable();

    var stream = _Stream(_WIDTH, _HEIGHT, codec, bitsPerPixel: 32);
    var (picture, matte) = alphaBits == 8
      ? _Rgba32(_WIDTH, _HEIGHT, static (x, y) => (byte)(((x / 8 + y / 8) & 1) == 0 ? 0 : 255))
      : _Rgba64(_WIDTH, _HEIGHT, static (x, y) => ((x / 8 + y / 8) & 1) == 0 ? (ushort)0 : ushort.MaxValue);

    var (planes, decoded) = _EncodeAndDecodeWithFfmpeg(stream, picture, "yuva444p12le");
    ProResPlaneComparison.AssertColour(decoded, planes, _WIDTH, _HEIGHT, chromaShift: 0, maximumDelta: 1);
    ProResPlaneComparison.AssertAlphaIsTheSourceMatte(decoded, matte, _WIDTH, _HEIGHT, alphaBits);
  }

  /// <summary>
  /// A wholly opaque matte, which is the one a broken alpha path is likeliest to survive.
  /// </summary>
  /// <remarks>
  /// A decoder fills an absent alpha plane with opacity, so an encoder which announced an alpha
  /// channel and then wrote nothing usable still produces a picture that looks right — as long as the
  /// matte it was given was opaque everywhere. Requiring the run coding to reproduce 255 and 65535
  /// exactly is what separates "wrote the matte" from "wrote nothing and got lucky": RDD 36 starts a
  /// slice from a previous alpha of −1, so full opacity is the value that codes to an escaped zero
  /// and the one an off-by-one in the modular difference gets wrong first.
  /// </remarks>
  [TestCase("ap4h", 8)]
  [TestCase("ap4h", 16)]
  [TestCase("ap4x", 8)]
  [TestCase("ap4x", 16)]
  [Category("Oracle")]
  public void FFmpegSeesAWhollyOpaqueMatteAsWhollyOpaque(string codec, int alphaBits) {
    FFmpegOracle.RequireAvailable();

    var stream = _Stream(_WIDTH, _HEIGHT, codec, bitsPerPixel: 32);
    var (picture, matte) = alphaBits == 8
      ? _Rgba32(_WIDTH, _HEIGHT, static (_, _) => byte.MaxValue)
      : _Rgba64(_WIDTH, _HEIGHT, static (_, _) => ushort.MaxValue);

    var (_, decoded) = _EncodeAndDecodeWithFfmpeg(stream, picture, "yuva444p12le");
    ProResPlaneComparison.AssertAlphaIsTheSourceMatte(decoded, matte, _WIDTH, _HEIGHT, alphaBits);

    var alphaOffset = _WIDTH * _HEIGHT * 3 * 2;
    for (var i = 0; i < _WIDTH * _HEIGHT; ++i)
      Assert.That(
        BinaryPrimitives.ReadUInt16LittleEndian(decoded.AsSpan(alphaOffset + i * 2)),
        Is.EqualTo(4095),
        $"sample {i} of an opaque matte reached ffmpeg as something other than full opacity");
  }

  /// <summary>A 4444 stream whose sample description states no alpha keeps the matte out of the frame.</summary>
  [TestCase("ap4h")]
  [TestCase("ap4x")]
  [Category("Oracle")]
  public void FFmpegReadsAFourFourFourStreamWithoutAlphaAsFullyOpaque(string codec) {
    FFmpegOracle.RequireAvailable();

    var stream = _Stream(_WIDTH, _HEIGHT, codec, bitsPerPixel: 24);
    var (picture, _) = _Rgba32(_WIDTH, _HEIGHT, static (x, y) => (byte)((x * 5 + y * 3) & 0xFF));

    var (planes, decoded) = _EncodeAndDecodeWithFfmpeg(stream, picture, "yuva444p12le");
    Assert.That(planes.Alpha, Is.Null, "a 24-bit sample description asks for colour only");

    var alphaOffset = _WIDTH * _HEIGHT * 3 * 2;
    for (var i = 0; i < _WIDTH * _HEIGHT; ++i)
      Assert.That(
        BinaryPrimitives.ReadUInt16LittleEndian(decoded.AsSpan(alphaOffset + i * 2)),
        Is.EqualTo(4095),
        $"sample {i} of a frame with no alpha channel should reach ffmpeg as full opacity");
  }

  /// <summary>Interlaced 4444 with a matte: field mapping and alpha coding in the same frame.</summary>
  [TestCase(0x0201)]
  [TestCase(0x0206)]
  [Category("Oracle")]
  public void FFmpegMatchesAnInterlacedMatteExactly(int fieldCode) {
    FFmpegOracle.RequireAvailable();

    const int HEIGHT = 50;
    var stream = _InterlacedStream(_WIDTH, HEIGHT, "ap4h", (ushort)fieldCode, bitsPerPixel: 32);

    // A matte that changes on every row is what a field mapping which lost a field's parity, or
    // repeated one field's matte into the other, cannot reproduce.
    var (picture, matte) = _Rgba32(_WIDTH, HEIGHT, static (x, y) => (byte)((y * 37 + x) & 0xFF));

    var (planes, decoded) = _EncodeAndDecodeWithFfmpeg(stream, picture, "yuva444p12le");
    ProResPlaneComparison.AssertColour(decoded, planes, _WIDTH, HEIGHT, chromaShift: 0, maximumDelta: 1);
    ProResPlaneComparison.AssertAlphaIsTheSourceMatte(decoded, matte, _WIDTH, HEIGHT, alphaBitDepth: 8);
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
    var movie = Path.Combine(directory.FullName, "frame.mov");
    try {
      File.WriteAllBytes(movie, VideoIO.Mux<Mp4Writer>([described], [packet]));

      return (planes, ProResFFmpegFixtures.DecodeToRaw(movie, pixelFormat));
    } finally {
      ProResFFmpegFixtures.Discard(movie);
    }
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

  private static MediaStreamInfo _InterlacedStream(
    int width, int height, string codec, ushort fieldCode, int bitsPerPixel) {
    var entry = new byte[96];
    BinaryPrimitives.WriteUInt32BigEndian(entry, (uint)entry.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(4), CodecTag.FromCharacters(codec).Value);
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(14), 1);
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(32), (ushort)width);
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(34), (ushort)height);
    BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(36), 0x00480000);
    BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(40), 0x00480000);
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(48), 1);
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(82), (ushort)bitsPerPixel);
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
      BitsPerPixel = bitsPerPixel,
      CodecPrivateData = entry,
    };
  }

  /// <summary>
  /// The band-limited picture the read direction uses, as a frame this encoder takes.
  /// </summary>
  /// <remarks>
  /// The same generator on both sides on purpose: a source with a hard edge in it makes the two
  /// inverse transforms disagree by several levels through ringing alone, and a tolerance wide enough
  /// to absorb that is wide enough to absorb a defect as well.
  /// </remarks>
  private static (RawImage Picture, ushort[][] Source) _Smooth(int width, int height, bool fourFourFour) {
    var planes = ProResFFmpegFixtures.SmoothPlanes(
      width, height, fourFourFour, alpha: false, bitDepth: fourFourFour ? 12 : 10);
    var format = fourFourFour ? PixelFormat.Yuv444P12 : PixelFormat.Yuv422P10;

    return (_Planar(width, height, format, planes[0], planes[1], planes[2]), planes);
  }

  /// <summary>An eight-bit picture and the matte in it, returned apart so the matte can be asserted.</summary>
  private static (RawImage Picture, ushort[] Matte) _Rgba32(int width, int height, Func<int, int, byte> alpha) {
    var bytes = new byte[width * height * 4];
    var matte = new ushort[width * height];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 4;
        bytes[at] = (byte)(40 + (x * 3 + y) % 180);
        bytes[at + 1] = (byte)(50 + (x + y * 5) % 170);
        bytes[at + 2] = (byte)(60 + (x * 7 + y * 3) % 160);
        matte[y * width + x] = bytes[at + 3] = alpha(x, y);
      }
    return (new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = bytes }, matte);
  }

  /// <summary>A sixteen-bit picture and the matte in it.</summary>
  private static (RawImage Picture, ushort[] Matte) _Rgba64(int width, int height, Func<int, int, ushort> alpha) {
    var bytes = new byte[width * height * 8];
    var matte = new ushort[width * height];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 8;
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at), (ushort)(10000 + (x * 733 + y * 191) % 40000));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at + 2), (ushort)(12000 + (x * 293 + y * 857) % 38000));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at + 4), (ushort)(14000 + (x * 571 + y * 353) % 36000));
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at + 6), matte[y * width + x] = alpha(x, y));
      }
    return (new() { Width = width, Height = height, Format = PixelFormat.Rgba64, PixelData = bytes }, matte);
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
