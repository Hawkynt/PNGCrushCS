using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Indeo.Tests;

/// <summary>Bitstream, registry and round-trip checks for the Indeo 5 writer.</summary>
/// <remarks>
/// The public decoder returns RGB because that is the video package's display contract, but IV50
/// itself codes YVU9 planes. The important round-trip checks therefore use <see cref="Indeo5Decoder"/>
/// directly and compare those planes: a wrong chroma order or a wrong subsampling decision must not
/// be hidden by applying the opposite mistake again in RGB conversion.
/// </remarks>
[TestFixture]
public sealed class Indeo5VideoEncoderTests {

  // ============================================================================================
  // Stream description and registration
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheDescriptionIsAnIv50VfwStream() {
    var encoder = Indeo5VideoEncoder.Create(_Requested(64, 48));
    var stream = encoder.DescribeStream();
    var format = stream.CodecPrivateData.ToArray();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("IV50")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("IV50")));
      Assert.That(stream.CodecId, Is.EqualTo("V_MS/VFW/FOURCC"));
      Assert.That(stream.Width, Is.EqualTo(64));
      Assert.That(stream.Height, Is.EqualTo(48));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(24));
      Assert.That(format, Has.Length.EqualTo(40));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format), Is.EqualTo(40));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(4)), Is.EqualTo(64));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(8)), Is.EqualTo(48));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(format.AsSpan(14)), Is.EqualTo(24));
      Assert.That(format[16..20], Is.EqualTo("IV50"u8.ToArray()));
    });

    Assert.That(Indeo5VideoDecoder.Accepts(stream), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void TheEncoderIsRegisteredUnderIv50() {
    Assert.That(VideoFormatRegistry.AllEncoders.Select(static e => e.CodecName),
      Does.Contain("Intel Indeo Video Interactive 5"));

    var requested = _Requested(64, 48);
    Assert.That(VideoFormatRegistry.CanEncode(requested), Is.True);
    Assert.That(VideoFormatRegistry.CreateEncoder(requested), Is.InstanceOf<Indeo5VideoEncoder>());
  }

  // ============================================================================================
  // Bitstream primitives
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void BitWriterAndReaderAgreeAcrossByteBoundaries() {
    var writer = new IviBitWriter();
    writer.Write(0b10101, 5);
    writer.Write(0x12A5, 13);
    writer.Write(0x89ABCDEFu, 32);
    writer.Align();
    writer.WriteBytes([0x5A, 0xC3]);

    var reader = new IviBitReader(writer.ToArray());
    Assert.Multiple(() => {
      Assert.That(reader.Read(5), Is.EqualTo(0b10101));
      Assert.That(reader.Read(13), Is.EqualTo(0x12A5));
      Assert.That(reader.Read(32), Is.EqualTo(0x89ABCDEFu));
    });
    reader.Align();
    Assert.That(reader.Read(8), Is.EqualTo(0x5A));
    Assert.That(reader.Read(8), Is.EqualTo(0xC3));
  }

  [Test]
  [Category("Unit")]
  public void EveryBuiltInHuffmanSymbolWrittenIsReadBackByTheDecoderTable() {
    foreach (var descriptors in new[] { IviTables.MacroblockDescriptors, IviTables.BlockDescriptors })
      foreach (var descriptor in descriptors) {
        var encoder = IviHuffmanEncoder.FromDescriptor(descriptor);
        var decoder = IviHuffmanTable.FromDescriptor(descriptor);
        var symbols = _SymbolCount(descriptor);

        for (var symbol = 0; symbol < symbols; ++symbol) {
          var writer = new IviBitWriter();
          encoder.Write(writer, symbol);
          var reader = new IviBitReader(writer.ToArray());

          Assert.That(decoder.Read(reader), Is.EqualTo(symbol),
            $"descriptor [{string.Join(",", descriptor)}], symbol {symbol}");
        }
      }
  }

  [Test]
  [Category("Unit")]
  public void TheForwardSlantIsTheInverseTransformWithinItsIntegerRounding() {
    var random = new Random(0x1D50);
    var totalError = 0L;
    var samplesChecked = 0;
    var largestError = 0;

    for (var block = 0; block < 256; ++block) {
      var samples = new int[64];
      for (var i = 0; i < samples.Length; ++i)
        samples[i] = random.Next(-128, 128);

      var coefficients = new int[64];
      IviForwardTransforms.Slant8x8(samples, coefficients);

      var columns = new byte[8];
      for (var y = 0; y < 8; ++y)
        for (var x = 0; x < 8; ++x)
          if (coefficients[y * 8 + x] != 0)
            columns[x] = 1;

      var reconstructed = new short[64];
      IviTransforms.InverseSlant8x8(coefficients, reconstructed, 0, 8, columns);

      for (var i = 0; i < samples.Length; ++i) {
        var error = Math.Abs(samples[i] - reconstructed[i]);
        largestError = Math.Max(largestError, error);
        totalError += error;
        ++samplesChecked;
      }
    }

    Assert.Multiple(() => {
      Assert.That(largestError, Is.LessThanOrEqualTo(16), "worst inverse-rounding error");
      Assert.That((double)totalError / samplesChecked, Is.LessThan(4), "mean inverse-rounding error");
    });
  }

  // ============================================================================================
  // Packets and decoded planes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void EveryPacketIsAnIntraPictureAndFrameNumbersAdvance() {
    var encoder = Indeo5VideoEncoder.Create(_Requested(32, 32));
    var frame = _FlatYuv444(32, 32, 96, 48, 208);

    for (var number = 0; number < 3; ++number) {
      Assert.That(encoder.TryEncode(frame, number * 2, out var packet), Is.True);
      var reader = new IviBitReader(packet.Data);

      Assert.Multiple(() => {
        Assert.That(reader.Read(5), Is.EqualTo(0x1F), "picture start code");
        Assert.That(reader.Read(3), Is.EqualTo(Indeo5Decoder.FrameTypeIntra), "frame type");
        Assert.That(reader.Read(8), Is.EqualTo(number), "frame number");
        Assert.That(packet.IsKeyFrame, Is.True);
        Assert.That(packet.PresentationTimestamp, Is.EqualTo(number * 2));
        Assert.That(packet.DecodeTimestamp, Is.EqualTo(number * 2));
        Assert.That(packet.Duration, Is.EqualTo(1));
      });
    }

    Assert.That(encoder.Flush(), Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void FlatYvu9PlanesComeBackWithBlueAndRedInTheRightOrder() {
    const int width = 32;
    const int height = 32;
    var source = _FlatYuv444(width, height, luma: 96, blue: 43, red: 211);
    var picture = _DecodePlanes(width, height, source);

    Assert.Multiple(() => {
      Assert.That(picture.Luma, Is.All.EqualTo(96));
      Assert.That(picture.ChromaBlue, Is.All.EqualTo(43));
      Assert.That(picture.ChromaRed, Is.All.EqualTo(211));
    });
  }

  [TestCase(33, 17)]
  [TestCase(1, 1)]
  [TestCase(65, 49)]
  [Category("Unit")]
  public void PartialEdgeBlocksDecodeAtTheStatedPictureSize(int width, int height) {
    var source = _PatternYuv444(width, height);
    var picture = _DecodePlanes(width, height, source);
    var expected = _ExpectedYvu9(source);

    Assert.Multiple(() => {
      Assert.That(picture.Width, Is.EqualTo(width));
      Assert.That(picture.Height, Is.EqualTo(height));
      Assert.That(picture.ChromaWidth, Is.EqualTo((width + 3) >> 2));
      Assert.That(picture.ChromaHeight, Is.EqualTo((height + 3) >> 2));
      _AssertError(picture.Luma, expected.Luma, maximum: 32, mean: 8, "luma");
      _AssertError(picture.ChromaBlue, expected.Blue, maximum: 8, mean: 3, "blue chroma");
      _AssertError(picture.ChromaRed, expected.Red, maximum: 8, mean: 3, "red chroma");
    });
  }

  [Test]
  [Category("Unit")]
  public void SeparateEncodersProduceTheSameBytesForTheSamePicture() {
    var frame = _PatternYuv444(64, 48);
    var first = _Encode(64, 48, frame).Data.ToArray();
    var second = _Encode(64, 48, frame).Data.ToArray();

    Assert.That(second, Is.EqualTo(first));
  }

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AnAudioStreamIsRefused() {
    var failure = Assert.Throws<NotSupportedException>(() => Indeo5VideoEncoder.Create(new() {
      Index = 0,
      Kind = MediaStreamKind.Audio,
      Width = 32,
      Height = 32,
    }));

    Assert.That(failure.Message, Does.Contain("video stream"));
  }

  [TestCase(0, 32)]
  [TestCase(32, 0)]
  [TestCase(8192, 32)]
  [TestCase(32, 8192)]
  [Category("Unit")]
  public void ASizeTheIv50HeaderCannotStateIsRefused(int width, int height) {
    var failure = Assert.Throws<NotSupportedException>(() => Indeo5VideoEncoder.Create(_Requested(width, height)));

    Assert.That(failure.Message, Does.Contain($"{width}x{height}"));
  }

  [Test]
  [Category("Unit")]
  public void AFrameOfAnotherSizeIsRefused() {
    var encoder = Indeo5VideoEncoder.Create(_Requested(32, 32));
    var failure = Assert.Throws<InvalidDataException>(
      () => encoder.TryEncode(_FlatYuv444(16, 16, 128, 128, 128), 0, out _));

    Assert.That(failure.Message, Does.Contain("16x16"));
  }

  [Test]
  [Category("Unit")]
  public void AContradictoryVfwDepthIsRefused() {
    var requested = _Requested(32, 32);
    requested.BitsPerPixel = 16;

    var failure = Assert.Throws<NotSupportedException>(() => Indeo5VideoEncoder.Create(requested));
    Assert.That(failure.Message, Does.Contain("16 bits per pixel"));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static MediaStreamInfo _Requested(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("IV50"),
    Handler = CodecTag.FromCharacters("IV50"),
    Width = width,
    Height = height,
    BitsPerPixel = 24,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static CodedPacket _Encode(int width, int height, RawImage frame) {
    var encoder = Indeo5VideoEncoder.Create(_Requested(width, height));
    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);
    return packet;
  }

  private static IviPicture _DecodePlanes(int width, int height, RawImage frame) {
    var packet = _Encode(width, height, frame);
    return new Indeo5Decoder(width, height).Decode(packet.Data)
      ?? throw new AssertionException("An intra packet unexpectedly decoded as a null frame.");
  }

  private static int _SymbolCount(ReadOnlySpan<byte> descriptor) {
    var count = 0;
    foreach (var width in descriptor)
      count = Math.Min(256, count + (1 << width));

    return count;
  }

  private static RawImage _FlatYuv444(int width, int height, byte luma, byte blue, byte red) {
    var samples = width * height;
    var data = new byte[samples * 3];
    data.AsSpan(0, samples).Fill(luma);
    data.AsSpan(samples, samples).Fill(blue);
    data.AsSpan(samples * 2, samples).Fill(red);
    return new() { Width = width, Height = height, Format = PixelFormat.Yuv444P8, PixelData = data };
  }

  private static RawImage _PatternYuv444(int width, int height) {
    var samples = width * height;
    var data = new byte[samples * 3];
    var luma = data.AsSpan(0, samples);
    var blue = data.AsSpan(samples, samples);
    var red = data.AsSpan(samples * 2, samples);

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = y * width + x;
        luma[at] = (byte)((x * 7 + y * 11 + ((x ^ y) & 7) * 13) & 0xFF);
        blue[at] = (byte)((32 + x * 3 + y * 5) & 0xFF);
        red[at] = (byte)((220 - x * 5 + y * 3) & 0xFF);
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Yuv444P8, PixelData = data };
  }

  private static (byte[] Luma, byte[] Blue, byte[] Red) _ExpectedYvu9(RawImage source) {
    var width = source.Width;
    var height = source.Height;
    var samples = width * height;
    var chromaWidth = (width + 3) >> 2;
    var chromaHeight = (height + 3) >> 2;
    var luma = source.GetPlaneData(0)[..samples].ToArray();
    var blue = new byte[chromaWidth * chromaHeight];
    var red = new byte[chromaWidth * chromaHeight];

    _Subsample(source.GetPlaneData(1), blue);
    _Subsample(source.GetPlaneData(2), red);
    return (luma, blue, red);

    void _Subsample(ReadOnlySpan<byte> from, Span<byte> to) {
      for (var cy = 0; cy < chromaHeight; ++cy)
        for (var cx = 0; cx < chromaWidth; ++cx) {
          var firstX = cx << 2;
          var firstY = cy << 2;
          var lastX = Math.Min(firstX + 4, width);
          var lastY = Math.Min(firstY + 4, height);
          var sum = 0;
          var count = 0;

          for (var y = firstY; y < lastY; ++y)
            for (var x = firstX; x < lastX; ++x) {
              sum += from[y * width + x];
              ++count;
            }

          to[cy * chromaWidth + cx] = (byte)((sum + (count >> 1)) / count);
        }
    }
  }

  private static void _AssertError(
    ReadOnlySpan<byte> actual, ReadOnlySpan<byte> expected, int maximum, double mean, string name) {
    Assert.That(actual.Length, Is.EqualTo(expected.Length), $"{name} sample count");

    var largest = 0;
    var total = 0L;
    for (var i = 0; i < actual.Length; ++i) {
      var error = Math.Abs(actual[i] - expected[i]);
      largest = Math.Max(largest, error);
      total += error;
    }

    Assert.That(largest, Is.LessThanOrEqualTo(maximum), $"{name} maximum absolute error");
    Assert.That((double)total / Math.Max(1, actual.Length), Is.LessThan(mean), $"{name} mean absolute error");
  }
}
