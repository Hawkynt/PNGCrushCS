using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.ProRes.Tests;

[TestFixture]
public class ProResVideoEncoderAdvancedTests {

  [TestCase(8)]
  [TestCase(16)]
  [Category("Unit")]
  public void AlphaRunAndDifferenceCodingIsItsOwnExactInverse(int bitDepth) {
    const int WIDTH = 64;
    const int HEIGHT = 16;
    var maximum = bitDepth == 8 ? 0xFF : 0xFFFF;
    var source = new ushort[WIDTH * HEIGHT];

    for (var i = 0; i < source.Length; ++i)
      source[i] = i switch {
        < 300 => (ushort)maximum,
        < 500 => (ushort)(maximum / 2),
        < 700 => (ushort)((maximum / 2 + (i & 7)) & maximum),
        _ => (ushort)((i * 257 + 19) & maximum),
      };

    var coded = ProResAlpha.Encode(source, bitDepth, WIDTH, 0, 0, WIDTH, HEIGHT);
    var decoded = new ushort[source.Length];
    ProResAlpha.Decode(coded, bitDepth == 8 ? 1 : 2, decoded, WIDTH, HEIGHT, 0, 0, WIDTH, HEIGHT, 0, 1);

    Assert.That(decoded, Is.EqualTo(source).AsCollection);
  }

  [TestCase("ap4h")]
  [TestCase("ap4x")]
  [Category("Unit")]
  public void FourFourFourProfilesWriteVersionOneTwelveBitColour(string codec) {
    const int WIDTH = 41;
    const int HEIGHT = 25;
    var stream = _Stream(WIDTH, HEIGHT, codec, bitsPerPixel: 24);
    var encoder = ProResVideoEncoder.Create(stream);
    var picture = _Flat444(WIDTH, HEIGHT, 2048, 2048, 2048);

    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);
    var data = packet.Data.ToArray();
    var decoder = ProResVideoDecoder.Create(stream);
    var planes = decoder.DecodePlanes(data, out var header);

    Assert.Multiple(() => {
      Assert.That(data[11], Is.EqualTo(1), "4:4:4 requires bitstream version 1");
      Assert.That((data[20] >> 6) & 3, Is.EqualTo(3), "chroma_format 3 is 4:4:4");
      Assert.That(header.ChromaFormat, Is.EqualTo(3));
      Assert.That(header.AlphaChannelType, Is.Zero);
      Assert.That(planes.BitDepth, Is.EqualTo(12));
      Assert.That(planes.ChromaWidth, Is.EqualTo(planes.Width));
      Assert.That(planes.Luma[0], Is.EqualTo(2048));
      Assert.That(planes.Cb[0], Is.EqualTo(2048));
      Assert.That(planes.Cr[0], Is.EqualTo(2048));
    });
  }

  /// <summary>
  /// The 4:4:4 profiles over the geometries the 4:2:2 ones are walked over.
  /// </summary>
  /// <remarks>
  /// Sizes that are and are not a whole number of macroblocks, because right and bottom padding are
  /// where an encoder and a decoder most easily lay a picture out differently and still produce
  /// something that decodes. 4:4:4 reaches it by a different route than 4:2:2 — four chroma blocks a
  /// macroblock instead of two, and no horizontal subsampling to round the chroma width up from.
  /// </remarks>
  /// <summary>
  /// A slice whose coded alpha stops early is read as far as it goes, and says how far that was.
  /// </summary>
  /// <remarks>
  /// FFmpeg's ProRes 4444 encoder leaves the last sample of every alpha slice out of the file, up to
  /// and including 6.1, and refusing those frames would mean refusing years of real ProRes over one
  /// sample in a thousand. What is read instead is what FFmpeg's own decoder reads: zeroes past the
  /// end of the coded data, which both arrive at the same value from.
  /// <para/>
  /// Pinned here rather than only through the oracle, because whether the oracle can see it depends
  /// on which FFmpeg the machine has — a current one writes whole slices and the case never arises.
  /// The truncation is therefore made directly: a slice is coded, its tail is cut, and what comes
  /// back has to be the coded samples exactly, the count of the missing ones exactly, and the value
  /// a zero tail decodes to for those.
  /// </remarks>
  [TestCase(8)]
  [TestCase(16)]
  [Category("Unit")]
  public void AlphaCodedShortOfItsSliceIsReadAsFarAsItGoesAndCounted(int bitDepth) {
    const int WIDTH = 64;
    const int HEIGHT = 16;
    var maximum = bitDepth == 8 ? 0xFF : 0xFFFF;
    var source = new ushort[WIDTH * HEIGHT];
    for (var i = 0; i < source.Length; ++i)
      source[i] = (ushort)((100 + i * 3) & maximum);

    var coded = ProResAlpha.Encode(source, bitDepth, WIDTH, 0, 0, WIDTH, HEIGHT);

    var whole = new ushort[source.Length];
    var intact = ProResAlpha.Decode(coded, bitDepth == 8 ? 1 : 2, whole, WIDTH, HEIGHT, 0, 0, WIDTH, HEIGHT, 0, 1);

    Assert.Multiple(() => {
      Assert.That(intact, Is.Zero, "a whole slice is missing nothing");
      Assert.That(whole, Is.EqualTo(source).AsCollection);
    });

    // Every sample here is its own run, so cutting a byte cuts samples off the end rather than
    // corrupting a run in the middle. One byte, because the quirk this reads through is one missing
    // sample and the tolerance is deliberately not wide enough to paper over a damaged slice.
    var cut = coded[..^1];
    var partial = new ushort[source.Length];
    var missing = ProResAlpha.Decode(cut, bitDepth == 8 ? 1 : 2, partial, WIDTH, HEIGHT, 0, 0, WIDTH, HEIGHT, 0, 1);

    Assert.Multiple(() => {
      Assert.That(missing, Is.InRange(1, 4), "a byte off the end is a sample or two, and has to be counted");

      for (var i = 0; i < source.Length - missing; ++i)
        Assert.That(partial[i], Is.EqualTo(source[i]), $"sample {i} was in the coded data");
    });
  }

  /// <summary>A slice cut far enough back to be damaged rather than short is still refused.</summary>
  /// <remarks>
  /// The tolerance above exists for one absent sample per slice. It must not become a licence to
  /// invent a matte: a reader that answered a truncated slice with a plane of synthesised samples
  /// would produce a picture that looks decoded and is not, which is the failure this whole decoder
  /// is written against.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void AlphaCutFarBackIsRefusedRatherThanInvented() {
    const int WIDTH = 64;
    const int HEIGHT = 16;
    var source = new ushort[WIDTH * HEIGHT];
    for (var i = 0; i < source.Length; ++i)
      source[i] = (ushort)((100 + i * 3) & 0xFF);

    var coded = ProResAlpha.Encode(source, 8, WIDTH, 0, 0, WIDTH, HEIGHT);
    var target = new ushort[source.Length];

    Assert.Throws<InvalidDataException>(
      () => ProResAlpha.Decode(coded[..(coded.Length / 2)], 1, target, WIDTH, HEIGHT, 0, 0, WIDTH, HEIGHT, 0, 1));
  }

  [TestCase("ap4h")]
  [TestCase("ap4x")]
  [Category("Unit")]
  public void EveryFourFourFourProfileCodesEveryGeometryThisPackagesOwnDecoderReadsBack(string codec) {
    foreach (var (width, height) in new[] { (16, 16), (41, 25), (64, 48), (160, 82) }) {
      var source = new ushort[width * height];
      for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x)
          source[y * width + x] = (ushort)(512 + (x * 9 + y * 17) % 2048);

      var picture = _Planes(width, height, PixelFormat.Yuv444P12, source, source, source);
      var stream = _Stream(width, height, codec, bitsPerPixel: 24);
      var planes = _EncodeAndDecode(stream, picture, out var header);

      Assert.Multiple(() => {
        Assert.That(header.HorizontalSize, Is.EqualTo(width), $"{codec} {width}x{height}");
        Assert.That(header.VerticalSize, Is.EqualTo(height), $"{codec} {width}x{height}");
        Assert.That(header.ChromaFormat, Is.EqualTo(3), $"{codec} {width}x{height}");
        Assert.That(planes.BitDepth, Is.EqualTo(12), $"{codec} {width}x{height}");
        Assert.That(planes.Width, Is.GreaterThanOrEqualTo(width), $"{codec} {width}x{height}");
        Assert.That(planes.ChromaWidth, Is.EqualTo(planes.Width), $"{codec} {width}x{height}");
      });

      var worst = 0;
      for (var y = 0; y < height; ++y)
        for (var x = 0; x < width; ++x)
          worst = Math.Max(worst, Math.Abs(planes.Luma[y * planes.Width + x] - source[y * width + x]));

      Assert.That(worst, Is.LessThan(256), $"{codec} {width}x{height} lost the picture entirely");
    }
  }

  [Test]
  [Category("Unit")]
  public void EightBitAlphaSurvives4444Exactly() {
    const int WIDTH = 37;
    const int HEIGHT = 19;
    var rgba = new byte[WIDTH * HEIGHT * 4];
    var expected = new ushort[WIDTH * HEIGHT];

    for (var i = 0; i < expected.Length; ++i) {
      rgba[i * 4] = (byte)(i * 3);
      rgba[i * 4 + 1] = (byte)(i * 5);
      rgba[i * 4 + 2] = (byte)(i * 7);
      rgba[i * 4 + 3] = (byte)(expected[i] = (ushort)((i * 29 + 11) & 0xFF));
    }

    var picture = new RawImage { Width = WIDTH, Height = HEIGHT, Format = PixelFormat.Rgba32, PixelData = rgba };
    var stream = _Stream(WIDTH, HEIGHT, "ap4h", bitsPerPixel: 32);
    var planes = _EncodeAndDecode(stream, picture, out var header);

    Assert.Multiple(() => {
      Assert.That(header.AlphaChannelType, Is.EqualTo(1));
      Assert.That(planes.AlphaBitDepth, Is.EqualTo(8));
      Assert.That(planes.Alpha, Is.Not.Null);
    });

    _AssertAlpha(expected, planes.Alpha!, planes.Width, WIDTH, HEIGHT);
  }

  [Test]
  [Category("Unit")]
  public void SixteenBitAlphaSurvives4444Exactly() {
    const int WIDTH = 35;
    const int HEIGHT = 21;
    var rgba = new byte[WIDTH * HEIGHT * 8];
    var expected = new ushort[WIDTH * HEIGHT];

    for (var i = 0; i < expected.Length; ++i) {
      var at = i * 8;
      BinaryPrimitives.WriteUInt16BigEndian(rgba.AsSpan(at), (ushort)(1000 + i));
      BinaryPrimitives.WriteUInt16BigEndian(rgba.AsSpan(at + 2), (ushort)(2000 + i));
      BinaryPrimitives.WriteUInt16BigEndian(rgba.AsSpan(at + 4), (ushort)(3000 + i));
      BinaryPrimitives.WriteUInt16BigEndian(rgba.AsSpan(at + 6), expected[i] = (ushort)(i * 977 + 123));
    }

    var picture = new RawImage { Width = WIDTH, Height = HEIGHT, Format = PixelFormat.Rgba64, PixelData = rgba };
    var stream = _Stream(WIDTH, HEIGHT, "ap4x", bitsPerPixel: 32);
    var planes = _EncodeAndDecode(stream, picture, out var header);

    Assert.Multiple(() => {
      Assert.That(header.AlphaChannelType, Is.EqualTo(2));
      Assert.That(planes.AlphaBitDepth, Is.EqualTo(16));
      Assert.That(planes.Alpha, Is.Not.Null);
    });

    _AssertAlpha(expected, planes.Alpha!, planes.Width, WIDTH, HEIGHT);
  }

  [TestCase(0x0201, 1)]
  [TestCase(0x0206, 2)]
  [Category("Unit")]
  public void BothFieldOrdersWriteTwoIndependentFieldPictures(int fieldCode, int expectedMode) {
    const int WIDTH = 48;
    const int HEIGHT = 17;
    var stream = _InterlacedStream(WIDTH, HEIGHT, "apcn", (ushort)fieldCode);
    var luma = new ushort[WIDTH * HEIGHT];
    var chroma = new ushort[WIDTH / 2 * HEIGHT];
    Array.Fill(chroma, (ushort)512);

    for (var y = 0; y < HEIGHT; ++y)
      for (var x = 0; x < WIDTH; ++x)
        luma[y * WIDTH + x] = (ushort)(y % 2 == 0 ? 256 : 768);

    var picture = _Planes422(WIDTH, HEIGHT, luma, chroma, chroma);
    var encoder = ProResVideoEncoder.Create(stream);
    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);

    var decoder = ProResVideoDecoder.Create(stream);
    var planes = decoder.DecodePlanes(packet.Data, out var header);
    var described = encoder.DescribeStream().CodecPrivateData.ToArray();

    Assert.Multiple(() => {
      Assert.That(header.InterlaceMode, Is.EqualTo(expectedMode));
      Assert.That(described, Has.Length.EqualTo(96));
      Assert.That(described.AsSpan(90, 4).ToArray(), Is.EqualTo("fiel"u8.ToArray()).AsCollection);
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(described.AsSpan(94)), Is.EqualTo(fieldCode));
      for (var y = 0; y < HEIGHT; ++y)
        for (var x = 0; x < WIDTH; ++x)
          Assert.That(planes.Luma[y * planes.Width + x], Is.EqualTo(luma[y * WIDTH + x]), $"sample {x},{y}");
    });
  }

  [TestCase(0x0209)]
  [TestCase(0x020E)]
  [Category("Unit")]
  public void FieldOrdersWhoseCodingAndDisplayOrdersDifferAreRefused(int fieldCode) {
    var refusal = Assert.Throws<NotSupportedException>(() =>
      ProResVideoEncoder.Create(_InterlacedStream(48, 32, "apcn", (ushort)fieldCode)));

    Assert.That(refusal!.Message, Does.Contain($"0x{fieldCode:X4}").And.Contain("coded").And.Contain("display"));
  }

  [Test]
  [Category("Unit")]
  public void AVisualSampleEntryWhoseStatedSizeCutsOffItsChildrenIsRefused() {
    var entry = _InterlacedStream(48, 32, "apcn", 0x0201).CodecPrivateData.ToArray();
    BinaryPrimitives.WriteUInt32BigEndian(entry, 85);
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("apcn"),
      Width = 48,
      Height = 32,
      BitsPerPixel = 24,
      CodecPrivateData = entry,
    };

    var refusal = Assert.Throws<InvalidDataException>(() => ProResVideoEncoder.Create(stream));
    Assert.That(refusal!.Message, Does.Contain("85").And.Contain("96").And.Contain("child atoms"));
  }

  [Test]
  [Category("Unit")]
  public void DecoderAcceptsZeroStuffingAndRefusesNonZeroStuffing() {
    const int WIDTH = 32;
    const int HEIGHT = 32;
    var stream = _Stream(WIDTH, HEIGHT, "apcn");
    var encoder = ProResVideoEncoder.Create(stream);
    Assert.That(encoder.TryEncode(_Flat422(WIDTH, HEIGHT, 512, 512, 512), 0, out var packet), Is.True);

    var original = packet.Data.ToArray();
    var zeroStuffed = new byte[original.Length + 3];
    original.CopyTo(zeroStuffed, 0);
    BinaryPrimitives.WriteUInt32BigEndian(zeroStuffed, (uint)zeroStuffed.Length);

    var decoder = ProResVideoDecoder.Create(stream);
    Assert.DoesNotThrow(() => decoder.DecodePlanes(zeroStuffed, out _));

    zeroStuffed[^1] = 0x7F;
    var refusal = Assert.Throws<InvalidDataException>(() => decoder.DecodePlanes(zeroStuffed, out _));
    Assert.That(refusal!.Message, Does.Contain("stuffing").And.Contain("0x7F"));
  }

  private static ProResPlanes _EncodeAndDecode(MediaStreamInfo stream, RawImage picture, out ProResFrameHeader header) {
    var encoder = ProResVideoEncoder.Create(stream);
    Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True);
    return ProResVideoDecoder.Create(stream).DecodePlanes(packet.Data, out header);
  }

  private static void _AssertAlpha(ushort[] expected, ushort[] actual, int stride, int width, int height) {
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        Assert.That(actual[y * stride + x], Is.EqualTo(expected[y * width + x]), $"alpha {x},{y}");
  }

  private static MediaStreamInfo _Stream(int width, int height, string codec, int bitsPerPixel = 0) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters(codec),
    Width = width,
    Height = height,
    BitsPerPixel = bitsPerPixel,
  };

  private static MediaStreamInfo _InterlacedStream(int width, int height, string codec, ushort fieldCode) {
    var entry = new byte[96];
    BinaryPrimitives.WriteUInt32BigEndian(entry, (uint)entry.Length);
    BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(4), CodecTag.FromCharacters(codec).Value);
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(32), (ushort)width);
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(34), (ushort)height);
    BinaryPrimitives.WriteUInt32BigEndian(entry.AsSpan(86), 10);
    "fiel"u8.CopyTo(entry.AsSpan(90));
    BinaryPrimitives.WriteUInt16BigEndian(entry.AsSpan(94), fieldCode);

    return new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters(codec),
      Width = width,
      Height = height,
      BitsPerPixel = 24,
      CodecPrivateData = entry,
    };
  }

  private static RawImage _Flat422(int width, int height, ushort y, ushort cb, ushort cr) {
    var luma = new ushort[width * height];
    var chromaWidth = (width + 1) / 2;
    var blue = new ushort[chromaWidth * height];
    var red = new ushort[chromaWidth * height];
    Array.Fill(luma, y);
    Array.Fill(blue, cb);
    Array.Fill(red, cr);
    return _Planes422(width, height, luma, blue, red);
  }

  private static RawImage _Flat444(int width, int height, ushort y, ushort cb, ushort cr) {
    var luma = new ushort[width * height];
    var blue = new ushort[width * height];
    var red = new ushort[width * height];
    Array.Fill(luma, y);
    Array.Fill(blue, cb);
    Array.Fill(red, cr);
    return _Planes(width, height, PixelFormat.Yuv444P12, luma, blue, red);
  }

  private static RawImage _Planes422(int width, int height, ushort[] luma, ushort[] cb, ushort[] cr)
    => _Planes(width, height, PixelFormat.Yuv422P10, luma, cb, cr);

  private static RawImage _Planes(
    int width,
    int height,
    PixelFormat format,
    ushort[] luma,
    ushort[] cb,
    ushort[] cr) {
    var bytes = new byte[checked((luma.Length + cb.Length + cr.Length) * 2)];
    var at = 0;
    foreach (var plane in new[] { luma, cb, cr })
      foreach (var sample in plane) {
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(at), sample);
        at += 2;
      }

    return new() { Width = width, Height = height, Format = format, PixelData = bytes };
  }
}
