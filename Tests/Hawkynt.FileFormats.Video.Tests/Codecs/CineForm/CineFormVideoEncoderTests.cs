using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using NUnit.Framework;

namespace FileFormat.Codecs.CineForm.Tests;

/// <summary>The CineForm writer's inverse pieces, framing, refusals and decoder round trips.</summary>
[TestFixture]
public sealed class CineFormVideoEncoderTests {
  private static readonly CodecTag _Cfhd = CodecTag.FromCharacters("CFHD");

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 7,
    Kind = MediaStreamKind.Video,
    Codec = _Cfhd,
    Handler = _Cfhd,
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  // ============================================================================================
  // Inverses the encoder depends on
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ForwardAndInverseWaveletsRecoverEveryInteger() {
    var random = new Random(2073);

    for (var length = 6; length <= 64; length += 2)
    for (var attempt = 0; attempt < 40; ++attempt) {
      var source = new int[length];
      for (var i = 0; i < source.Length; ++i)
        source[i] = random.Next(-4096, 4097);

      var low = new int[length / 2];
      var high = new int[length / 2];
      var reconstructed = new int[length];
      CineFormWavelet.ForwardOneDimensional(source, low, high);
      CineFormWavelet.InverseOneDimensional(low, high, reconstructed);

      Assert.That(reconstructed, Is.EqualTo(source), $"length {length}, attempt {attempt}");
    }
  }

  [Test]
  [Category("Unit")]
  public void ForwardAndInverseSpatialWaveletsRecoverAPicture() {
    const int WIDTH = 24;
    const int HEIGHT = 16;
    var random = new Random(311);
    var source = Enumerable.Range(0, WIDTH * HEIGHT).Select(_ => random.Next(-2048, 2049)).ToArray();

    var bands = CineFormWavelet.ForwardSpatial(source, WIDTH, HEIGHT);
    var reconstructed = CineFormWavelet.InverseSpatial(
      bands.Ll, bands.Lh, bands.Hl, bands.Hh, WIDTH / 2, HEIGHT / 2, out var width, out var height);

    Assert.Multiple(() => {
      Assert.That(width, Is.EqualTo(WIDTH));
      Assert.That(height, Is.EqualTo(HEIGHT));
      Assert.That(reconstructed, Is.EqualTo(source));
    });
  }

  [Test]
  [Category("Unit")]
  public void EveryMagnitudeCodewordWrittenByTheEncoderIsReadByTheDecoder() {
    for (var magnitude = 0; magnitude <= 255; ++magnitude) {
      Assert.That(CineFormCodebook.TryGetCodeword(1, magnitude, out var code, out var length), Is.True, $"magnitude {magnitude}");
      var writer = new CineFormBitWriter();
      writer.WriteBits(code, length);
      var reader = new CineFormBitReader(writer.ToSegmentAlignedArray(), 0);

      Assert.That(CineFormCodebook.TryDecodeRun(reader, out var run, out var decoded), Is.True, $"magnitude {magnitude}");
      Assert.That(run, Is.EqualTo(1), $"magnitude {magnitude}");
      Assert.That(decoded, Is.EqualTo(magnitude), $"magnitude {magnitude}");
    }
  }

  [Test]
  [Category("Unit")]
  public void EntropyCodingPreservesValuesOnTheCompandingGrid() {
    const int WIDTH = 7;
    const int HEIGHT = 3;
    var coefficients = new int[WIDTH * HEIGHT];
    for (var i = 0; i < coefficients.Length; ++i) {
      var symbol = i % 17;
      var magnitude = CineFormWavelet.CompandedMagnitude(symbol);
      coefficients[i] = (i & 1) == 0 ? magnitude : -magnitude;
    }
    coefficients[3] = coefficients[4] = coefficients[5] = 0;

    var encoded = CineFormEntropyEncoder.Encode(coefficients, WIDTH, HEIGHT);
    Assert.That(encoded.Quantization, Is.EqualTo(1));

    var paddedWidth = (WIDTH + 7) & ~7;
    var decoded = new int[paddedWidth * HEIGHT];
    var reader = new CineFormBitReader(encoded.Data, 0);
    var at = 0;
    while (at < decoded.Length) {
      Assert.That(CineFormCodebook.TryDecodeRun(reader, out var run, out var value), Is.True);
      Assert.That(value, Is.Not.EqualTo(CineFormCodebook.BandEndMarkerValue));
      if (value == 0) {
        at += run;
        continue;
      }

      var sign = reader.Peek(1);
      reader.Advance(1);
      decoded[at++] = sign == 0 ? value : -value;
    }

    Assert.That(CineFormCodebook.TryDecodeRun(reader, out var endRun, out var end), Is.True);
    Assert.That(endRun, Is.EqualTo(0));
    Assert.That(end, Is.EqualTo(CineFormCodebook.BandEndMarkerValue));
    CineFormWavelet.Dequantize(decoded, encoded.Quantization);

    for (var y = 0; y < HEIGHT; ++y)
    for (var x = 0; x < WIDTH; ++x)
      Assert.That(decoded[y * paddedWidth + x], Is.EqualTo(coefficients[y * WIDTH + x]), $"{x},{y}");
  }

  // ============================================================================================
  // Stream description and packet framing
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void DescribesAStreamTheCineFormDecoderAccepts() {
    var described = CineFormVideoEncoder.Create(_Stream(64, 48)).DescribeStream();

    Assert.Multiple(() => {
      Assert.That(CineFormVideoEncoder.Codec, Is.EqualTo(_Cfhd));
      Assert.That(described.Codec, Is.EqualTo(_Cfhd));
      Assert.That(described.Handler, Is.EqualTo(_Cfhd));
      Assert.That(described.Index, Is.EqualTo(7));
      Assert.That(described.Width, Is.EqualTo(64));
      Assert.That(described.Height, Is.EqualTo(48));
      Assert.That(described.BitsPerPixel, Is.EqualTo(20));
      Assert.That(CineFormVideoDecoder.Accepts(described), Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void PacketStartsWithTheVendorIFrameHeaderAndAThreeChannelIndex() {
    var encoder = CineFormVideoEncoder.Create(_Stream(64, 48));
    var frame = _Flat(64, 48, 400, 500, 600);
    var packet = encoder.EncodeFrame(frame, 0x1234);

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(packet), Is.EqualTo(1), "SampleType tag");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(2)), Is.EqualTo(9), "I-frame sample type");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(4)), Is.EqualTo(2), "SampleIndexTable tag");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(6)), Is.EqualTo(3), "three indexed channels");
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(8)), Is.GreaterThan(0));
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(12)), Is.GreaterThan(0));
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(packet.AsSpan(16)), Is.GreaterThan(0));
    });
  }

  // ============================================================================================
  // Round trips through this package's independent decoder
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFlatTenBitPictureComesBackSampleForSample() {
    const int WIDTH = 64;
    const int HEIGHT = 48;
    var encoder = CineFormVideoEncoder.Create(_Stream(WIDTH, HEIGHT));
    var frame = _Flat(WIDTH, HEIGHT, 700, 300, 800);

    Assert.That(encoder.TryEncode(frame, 17, out var packet), Is.True);
    var result = CineFormVideoDecoder.Create(encoder.DescribeStream()).DecodeChannels(packet.Data);

    Assert.Multiple(() => {
      Assert.That(result.IsYuv, Is.True);
      Assert.That(result.ImageWidth, Is.EqualTo(WIDTH));
      Assert.That(result.ImageHeight, Is.EqualTo(HEIGHT));
      Assert.That(result.Channels[0].Samples.Take(WIDTH * HEIGHT), Is.All.EqualTo(700), "Y");
      Assert.That(result.Channels[1].Samples.Take(WIDTH / 2 * HEIGHT), Is.All.EqualTo(800), "V is channel 1");
      Assert.That(result.Channels[2].Samples.Take(WIDTH / 2 * HEIGHT), Is.All.EqualTo(300), "U is channel 2");
    });
  }

  [Test]
  [Category("Unit")]
  public void DisplayHeightRemovesTheEightLineCodingPad() {
    const int WIDTH = 64;
    const int HEIGHT = 45;
    var encoder = CineFormVideoEncoder.Create(_Stream(WIDTH, HEIGHT));
    var frame = _Flat(WIDTH, HEIGHT, 512, 512, 512);

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);
    var result = CineFormVideoDecoder.Create(encoder.DescribeStream()).DecodeChannels(packet.Data);

    Assert.Multiple(() => {
      Assert.That(result.ImageHeight, Is.EqualTo(HEIGHT));
      Assert.That(result.Channels[0].Height, Is.EqualTo(48), "the coded plane retains its padded rows");
    });
  }

  [Test]
  [Category("Unit")]
  public void AStructuredPictureSurvivesWithBoundedWaveletLoss() {
    const int WIDTH = 64;
    const int HEIGHT = 48;
    var (frame, y, cb, cr) = _Ramp(WIDTH, HEIGHT);
    var encoder = CineFormVideoEncoder.Create(_Stream(WIDTH, HEIGHT));
    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);

    var result = CineFormVideoDecoder.Create(encoder.DescribeStream()).DecodeChannels(packet.Data);
    var yError = _WorstError(y, result.Channels[0].Samples, WIDTH, WIDTH, HEIGHT);
    var cbError = _WorstError(cb, result.Channels[2].Samples, WIDTH / 2, WIDTH / 2, HEIGHT);
    var crError = _WorstError(cr, result.Channels[1].Samples, WIDTH / 2, WIDTH / 2, HEIGHT);

    Assert.Multiple(() => {
      Assert.That(yError, Is.LessThanOrEqualTo(16), "luma");
      Assert.That(cbError, Is.LessThanOrEqualTo(16), "Cb");
      Assert.That(crError, Is.LessThanOrEqualTo(16), "Cr");
    });
  }

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void RefusesGeometryTheThreeLevelWriterCannotRepresent() {
    Assert.Throws<NotSupportedException>(() => CineFormVideoEncoder.Create(_Stream(32, 48)));
    Assert.Throws<NotSupportedException>(() => CineFormVideoEncoder.Create(_Stream(63, 48)));
    Assert.Throws<NotSupportedException>(() => CineFormVideoEncoder.Create(_Stream(64, 0)));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAWrongSizedOrTruncatedPicture() {
    var encoder = CineFormVideoEncoder.Create(_Stream(64, 48));
    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Flat(64, 40, 100, 100, 100), 0, out _));

    var shortFrame = new RawImage { Width = 64, Height = 48, Format = PixelFormat.Yuv422P10, PixelData = new byte[10] };
    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(shortFrame, 0, out _));
  }

  [Test]
  [Category("Unit")]
  public void RefusesATenBitPlaneContaining1024() {
    var encoder = CineFormVideoEncoder.Create(_Stream(64, 48));
    var frame = _Flat(64, 48, 100, 100, 100);
    BinaryPrimitives.WriteUInt16LittleEndian(frame.PixelData, 1024);

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(frame, 0, out _));
    Assert.That(failure!.Message, Does.Contain("1024"));
  }

  private static RawImage _Flat(int width, int height, ushort y, ushort cb, ushort cr) {
    var luma = Enumerable.Repeat(y, width * height).ToArray();
    var chromaWidth = width / 2;
    var blue = Enumerable.Repeat(cb, chromaWidth * height).ToArray();
    var red = Enumerable.Repeat(cr, chromaWidth * height).ToArray();
    return _Planes(width, height, luma, blue, red);
  }

  private static (RawImage Frame, ushort[] Y, ushort[] Cb, ushort[] Cr) _Ramp(int width, int height) {
    var chromaWidth = width / 2;
    var y = new ushort[width * height];
    var cb = new ushort[chromaWidth * height];
    var cr = new ushort[chromaWidth * height];

    for (var row = 0; row < height; ++row) {
      for (var x = 0; x < width; ++x)
        y[row * width + x] = (ushort)(64 + (x * 700 / (width - 1)) + row % 7);
      for (var x = 0; x < chromaWidth; ++x) {
        cb[row * chromaWidth + x] = (ushort)(300 + x * 300 / Math.Max(1, chromaWidth - 1));
        cr[row * chromaWidth + x] = (ushort)(700 - x * 300 / Math.Max(1, chromaWidth - 1));
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
}
