using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video.Tests;
using NUnit.Framework;

namespace FileFormat.Codecs.CineForm.Tests;

[TestFixture]
public sealed class CineFormInterlacedTests {
  private static readonly CodecTag _Cfhd = CodecTag.FromCharacters("CFHD");

  [TestCase(CineFormScanMode.InterlacedUpperFieldFirst, 3, true)]
  [TestCase(CineFormScanMode.InterlacedLowerFieldFirst, 1, false)]
  [Category("Unit")]
  public void HeaderCarriesInterlaceAndFieldOrder(
    CineFormScanMode scanMode,
    int expectedInterlaceFlags,
    bool expectedUpperFirst) {

    var encoder = CineFormVideoEncoder.Create(_Stream(64, 48), CineFormEncodingFormat.Yuv422, scanMode);
    var frame = _Fields(64, 48);
    var packet = encoder.EncodeFrame(frame);
    var decoded = CineFormVideoDecoder.Create(encoder.DescribeStream()).DecodeChannels(packet);

    Assert.Multiple(() => {
      Assert.That(_HeaderValue(packet, CineFormTags.TransformType), Is.Zero);
      Assert.That(_HeaderValue(packet, CineFormTags.SampleFlags), Is.Zero);
      Assert.That(_HeaderValue(packet, CineFormTags.InterlacedFlags), Is.EqualTo(expectedInterlaceFlags));
      Assert.That(_HeaderValue(packet, CineFormTags.SubbandCount), Is.EqualTo(10));
      Assert.That(decoded.IsInterlaced, Is.True);
      Assert.That(decoded.UpperFieldFirst, Is.EqualTo(expectedUpperFirst));
    });
  }

  [Test]
  [Category("Unit")]
  public void AlternatingFieldsSurviveTheInterlacedTransform() {
    const int WIDTH = 64;
    const int HEIGHT = 48;
    var encoder = CineFormVideoEncoder.Create(
      _Stream(WIDTH, HEIGHT),
      CineFormEncodingFormat.Yuv422,
      CineFormScanMode.InterlacedUpperFieldFirst);
    var (frame, y, cb, cr) = _FieldsWithPlanes(WIDTH, HEIGHT);

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);
    var decoded = CineFormVideoDecoder.Create(encoder.DescribeStream()).DecodeChannels(packet.Data);

    Assert.Multiple(() => {
      Assert.That(decoded.IsInterlaced, Is.True);
      Assert.That(_WorstError(y, decoded.Channels[0].Samples, WIDTH, WIDTH, HEIGHT), Is.LessThanOrEqualTo(16), "luma");
      Assert.That(_WorstError(cb, decoded.Channels[2].Samples, WIDTH / 2, WIDTH / 2, HEIGHT), Is.LessThanOrEqualTo(16), "Cb");
      Assert.That(_WorstError(cr, decoded.Channels[1].Samples, WIDTH / 2, WIDTH / 2, HEIGHT), Is.LessThanOrEqualTo(16), "Cr");
    });

    // The two fields deliberately live hundreds of levels apart. A progressive reconstruction of
    // this packet would smear that difference vertically; checking representative even/odd lines is
    // therefore more useful than merely proving that some picture came back.
    for (var row = 0; row < HEIGHT; ++row) {
      var expected = row % 2 == 0 ? 180 : 820;
      Assert.That(decoded.Channels[0].Samples[row * WIDTH + WIDTH / 2], Is.InRange(expected - 16, expected + 16), $"row {row}");
    }
  }

  [Test]
  [Category("Unit")]
  public void InterlacedPaddingPreservesEachFieldsLastLine() {
    const int WIDTH = 64;
    const int HEIGHT = 46;
    var encoder = CineFormVideoEncoder.Create(
      _Stream(WIDTH, HEIGHT),
      CineFormEncodingFormat.Yuv422,
      CineFormScanMode.InterlacedUpperFieldFirst);

    Assert.That(encoder.TryEncode(_Fields(WIDTH, HEIGHT), 0, out var packet), Is.True);
    var decoded = CineFormVideoDecoder.Create(encoder.DescribeStream()).DecodeChannels(packet.Data);
    var luma = decoded.Channels[0];

    Assert.Multiple(() => {
      Assert.That(decoded.ImageHeight, Is.EqualTo(HEIGHT));
      Assert.That(luma.Height, Is.EqualTo(48));
      Assert.That(luma.Samples[46 * WIDTH + WIDTH / 2], Is.InRange(164, 196), "padded even line repeats the even field");
      Assert.That(luma.Samples[47 * WIDTH + WIDTH / 2], Is.InRange(804, 836), "padded odd line repeats the odd field");
    });
  }

  [Test]
  [Category("Unit")]
  public void RefusesOddHeightAndInterlacedRgb() {
    Assert.Throws<NotSupportedException>(() => CineFormVideoEncoder.Create(
      _Stream(64, 47), CineFormEncodingFormat.Yuv422, CineFormScanMode.InterlacedUpperFieldFirst));

    Assert.Throws<NotSupportedException>(() => CineFormVideoEncoder.Create(
      _Stream(64, 48), CineFormEncodingFormat.Rgb444, CineFormScanMode.InterlacedUpperFieldFirst));
  }

  [Test]
  [Category("Conformance")]
  public void FfmpegReadsAnInterlacedAviWhenAvailable() {
    FFmpegOracle.RequireAvailable();

    const int WIDTH = 64;
    const int HEIGHT = 48;
    var encoder = CineFormVideoEncoder.Create(
      _Stream(WIDTH, HEIGHT),
      CineFormEncodingFormat.Yuv422,
      CineFormScanMode.InterlacedUpperFieldFirst);
    Assert.That(encoder.TryEncode(_Fields(WIDTH, HEIGHT), 0, out var packet), Is.True);

    var avi = VideoIO.Mux<AviWriter>([encoder.DescribeStream()], [packet]);
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");
    try {
      File.WriteAllBytes(path, avi);
      var (decoded, output) = FFmpegOracle.TryDecodeFirstFrame(path, WIDTH, HEIGHT);
      Assert.That(decoded, Is.True, output);
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }
  }

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = _Cfhd,
    Handler = _Cfhd,
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _Fields(int width, int height) => _FieldsWithPlanes(width, height).Frame;

  private static (RawImage Frame, ushort[] Y, ushort[] Cb, ushort[] Cr) _FieldsWithPlanes(int width, int height) {
    var chromaWidth = width / 2;
    var y = new ushort[width * height];
    var cb = new ushort[chromaWidth * height];
    var cr = new ushort[chromaWidth * height];

    for (var row = 0; row < height; ++row) {
      var even = (row & 1) == 0;
      for (var x = 0; x < width; ++x)
        y[row * width + x] = (ushort)((even ? 180 : 820) + x % 5);
      for (var x = 0; x < chromaWidth; ++x) {
        cb[row * chromaWidth + x] = (ushort)((even ? 360 : 680) + x % 3);
        cr[row * chromaWidth + x] = (ushort)((even ? 700 : 300) + x % 3);
      }
    }

    return (_Planes(width, height, y, cb, cr), y, cb, cr);
  }

  private static RawImage _Planes(int width, int height, ushort[] y, ushort[] cb, ushort[] cr) {
    var data = new byte[(y.Length + cb.Length + cr.Length) * 2];
    var offset = 0;
    foreach (var plane in new[] { y, cb, cr })
    foreach (var sample in plane) {
      BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), sample);
      offset += 2;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Yuv422P10, PixelData = data };
  }

  private static int _WorstError(ushort[] expected, int[] actual, int expectedStride, int actualStride, int height) {
    var worst = 0;
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < expectedStride; ++x)
      worst = Math.Max(worst, Math.Abs(expected[y * expectedStride + x] - actual[y * actualStride + x]));
    return worst;
  }

  private static int _HeaderValue(ReadOnlySpan<byte> packet, int wantedTag) {
    var position = 0;
    while (position + 4 <= packet.Length) {
      var tag = (short)BinaryPrimitives.ReadUInt16BigEndian(packet[position..]);
      var value = BinaryPrimitives.ReadUInt16BigEndian(packet[(position + 2)..]);
      position += 4;
      if (Math.Abs(tag) == wantedTag)
        return value;
      if (tag == CineFormTags.Index)
        position += checked(value * 4);
      if (Math.Abs(tag) == CineFormTags.LowpassPrecision)
        break;
    }

    throw new InvalidDataException($"CineForm packet did not contain header tag {wantedTag}.");
  }
}
