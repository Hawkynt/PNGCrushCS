using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Vc1.Tests;

/// <summary>The VC-1 writer: progressive Main-profile I/P/B pictures with reconstructed references.</summary>
[TestFixture]
public sealed class Vc1VideoEncoderTests {

  [Test]
  [Category("Unit")]
  public void DescribesTheMainProfileSubsetItWrites() {
    var encoder = Vc1VideoEncoder.Create(_Requested(20, 12));
    var stream = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(CodecTag.FromCharacters("WMV3")));
      Assert.That(stream.Handler, Is.EqualTo(CodecTag.FromCharacters("WMV3")));
      Assert.That(stream.CodecId, Is.EqualTo("V_MS/VFW/FOURCC"));
      Assert.That(stream.Width, Is.EqualTo(20));
      Assert.That(stream.Height, Is.EqualTo(12));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(24));
      Assert.That(stream.CodecPrivateData.Length, Is.EqualTo(44));
    });

    var format = stream.CodecPrivateData.ToArray();
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan()), Is.EqualTo(44), "biSize includes STRUCT_C");
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(4)), Is.EqualTo(20), "biWidth");
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(8)), Is.EqualTo(12), "biHeight");
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(format.AsSpan(14)), Is.EqualTo(24), "biBitCount");
      Assert.That(format[16..20], Is.EqualTo("WMV3"u8.ToArray()), "biCompression");
    });

    var sequence = Vc1SequenceHeader.ReadFrom(format.AsSpan(40));
    Assert.Multiple(() => {
      Assert.That(sequence.Profile, Is.EqualTo(Vc1Profile.Main));
      Assert.That(sequence.Quantiser, Is.EqualTo(3));
      Assert.That(sequence.MaxBFrames, Is.EqualTo(1));
      Assert.That(sequence.LoopFilter, Is.False);
      Assert.That(sequence.MultiResolution, Is.False);
      Assert.That(sequence.Overlap, Is.False);
      Assert.That(sequence.RangeReduction, Is.False);
    });

    Assert.That(Vc1VideoDecoder.Create(stream), Is.Not.Null);
  }

  [Test]
  [Category("Unit")]
  public void TheEncoderIsRegisteredUnderTheDecoderName() {
    var requested = _Requested(16, 16, codec: "WMV3");

    Assert.That(
      VideoFormatRegistry.AllEncoders.Select(e => e.CodecName),
      Does.Contain(Vc1VideoDecoder.CodecName));
    Assert.That(VideoFormatRegistry.CanEncode(requested), Is.True);
    Assert.That(VideoFormatRegistry.CreateEncoder(requested), Is.InstanceOf<Vc1VideoEncoder>());
  }

  [TestCase(16, 16)]
  [TestCase(17, 19)]
  [Category("Unit")]
  public void FlatMidGreyRoundTripsExactly(int width, int height) {
    var encoder = Vc1VideoEncoder.Create(_Requested(width, height));
    Assert.That(encoder.TryEncode(_Flat(width, height, 128), 7, out var packet), Is.True);

    var decoder = Vc1VideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);

    Assert.Multiple(() => {
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(7));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(7));
      Assert.That(decoded.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(decoded.PixelData, Is.All.EqualTo((byte)128));
    });
  }

  [Test]
  [Category("Unit")]
  public void SpatialDetailIsCarriedByAcCoefficients() {
    const int width = 32;
    const int height = 16;
    var source = _GreyscaleDetail(width, height);
    var encoder = Vc1VideoEncoder.Create(_Requested(width, height));

    Assert.That(encoder.TryEncode(source, 0, out var detailed), Is.True);

    var flatEncoder = Vc1VideoEncoder.Create(_Requested(width, height));
    Assert.That(flatEncoder.TryEncode(_Flat(width, height, 128), 0, out var flat), Is.True);
    Assert.That(detailed.Data.Length, Is.GreaterThan(flat.Data.Length), "non-flat blocks must carry AC syntax");

    var decoder = Vc1VideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(detailed, out var decoded), Is.True);

    var maximumError = 0;
    long totalError = 0;
    for (var i = 0; i < source.PixelData.Length; ++i) {
      var error = Math.Abs(source.PixelData[i] - decoded.PixelData[i]);
      maximumError = Math.Max(maximumError, error);
      totalError += error;
    }

    Assert.Multiple(() => {
      Assert.That(maximumError, Is.LessThanOrEqualTo(7), "uniform quantiser 3 should preserve greyscale detail closely");
      Assert.That((double)totalError / source.PixelData.Length, Is.LessThan(1.5), "AC coding must beat block-average reconstruction");
    });
  }

  [Test]
  [Category("Unit")]
  public void PicturesAreCodedInAnchorOrderAndDisplayedInPresentationOrder() {
    var encoder = Vc1VideoEncoder.Create(_Requested(16, 16));

    Assert.That(encoder.TryEncode(_Flat(16, 16, 128), 0, out var intra), Is.True);
    Assert.That(encoder.TryEncode(_Flat(16, 16, 160), 1, out _), Is.False, "the B picture needs its future anchor first");
    Assert.That(encoder.TryEncode(_Flat(16, 16, 192), 2, out var predicted), Is.True);
    var bidirectional = encoder.Flush().Single();

    var sequence = Vc1SequenceHeader.ReadFrom(encoder.DescribeStream().CodecPrivateData.Span[40..]);
    var pReader = new Vc1BitReader(predicted.Data.Span);
    var bReader = new Vc1BitReader(bidirectional.Data.Span);
    var pHeader = Vc1PictureHeader.ReadFrom(ref pReader, sequence);
    var bHeader = Vc1PictureHeader.ReadFrom(ref bReader, sequence);

    Assert.Multiple(() => {
      Assert.That(intra.PresentationTimestamp, Is.EqualTo(0));
      Assert.That(predicted.PresentationTimestamp, Is.EqualTo(2));
      Assert.That(bidirectional.PresentationTimestamp, Is.EqualTo(1));
      Assert.That(intra.DecodeTimestamp, Is.EqualTo(0));
      Assert.That(predicted.DecodeTimestamp, Is.EqualTo(1));
      Assert.That(bidirectional.DecodeTimestamp, Is.EqualTo(2));
      Assert.That(intra.IsKeyFrame, Is.True);
      Assert.That(predicted.IsKeyFrame, Is.False);
      Assert.That(bidirectional.IsKeyFrame, Is.False);
      Assert.That(pHeader.PictureType, Is.EqualTo(Vc1PictureType.Predicted));
      Assert.That(bHeader.PictureType, Is.EqualTo(Vc1PictureType.Bidirectional));
      Assert.That(bHeader.BFractionNumerator, Is.EqualTo(1));
      Assert.That(bHeader.BFractionDenominator, Is.EqualTo(2));
    });

    var decoder = Vc1VideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(intra, out var first), Is.True);
    Assert.That(decoder.TryDecode(predicted, out _), Is.False, "the future anchor is held until the B picture is displayed");
    Assert.That(decoder.TryDecode(bidirectional, out var second), Is.True);
    var third = decoder.Flush().Single();

    Assert.Multiple(() => {
      Assert.That(_Mean(first), Is.EqualTo(128).Within(8));
      Assert.That(_Mean(second), Is.EqualTo(160).Within(8));
      Assert.That(_Mean(third), Is.EqualTo(192).Within(8));
    });
  }

  [Test]
  [Category("Unit")]
  public void AFinalUnpairedPictureFlushesAsAPredictedAnchor() {
    var encoder = Vc1VideoEncoder.Create(_Requested(16, 16));

    Assert.That(encoder.TryEncode(_Flat(16, 16, 128), 0, out var intra), Is.True);
    Assert.That(encoder.TryEncode(_Flat(16, 16, 176), 1, out _), Is.False);
    var predicted = encoder.Flush().Single();

    var sequence = Vc1SequenceHeader.ReadFrom(encoder.DescribeStream().CodecPrivateData.Span[40..]);
    var reader = new Vc1BitReader(predicted.Data.Span);
    var header = Vc1PictureHeader.ReadFrom(ref reader, sequence);
    Assert.Multiple(() => {
      Assert.That(header.PictureType, Is.EqualTo(Vc1PictureType.Predicted));
      Assert.That(predicted.PresentationTimestamp, Is.EqualTo(1));
      Assert.That(predicted.IsKeyFrame, Is.False);
    });

    var decoder = Vc1VideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(intra, out _), Is.True);
    Assert.That(decoder.TryDecode(predicted, out _), Is.False);
    var decoded = decoder.Flush().Single();
    Assert.That(_Mean(decoded), Is.EqualTo(176).Within(8));
  }

  [Test]
  [Category("Unit")]
  public void ASkippedPictureRepeatsThePreviousDecodedPictureWithoutAliasingCallerData() {
    var encoder = Vc1VideoEncoder.Create(_Requested(16, 16));
    Assert.That(encoder.TryEncode(_Flat(16, 16, 128), 0, out var coded), Is.True);

    var decoder = Vc1VideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(coded, out var first), Is.True);
    var expected = (byte[])first.PixelData.Clone();

    Array.Fill(first.PixelData, (byte)0);
    Assert.That(decoder.TryDecode(new(0, new byte[1]), out _), Is.False, "with B pictures enabled an anchor is delayed");
    var repeated = decoder.Flush().Single();

    Assert.Multiple(() => {
      Assert.That(repeated.PixelData, Is.EqualTo(expected));
      Assert.That(repeated.PixelData, Is.Not.SameAs(first.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void AGeometryChangeMidStreamIsRefused() {
    var encoder = Vc1VideoEncoder.Create(_Requested(16, 16));

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Flat(8, 8, 128), 0, out _));
    Assert.That(failure!.Message, Does.Contain("16x16"));
  }

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsRefused()
    => Assert.Throws<NotSupportedException>(() => Vc1VideoEncoder.Create(_Requested(16, 16, kind: MediaStreamKind.Audio)));

  [Test]
  [Category("Unit")]
  public void AStreamWithNoGeometryIsRefused() {
    var failure = Assert.Throws<NotSupportedException>(() => Vc1VideoEncoder.Create(_Requested(0, 16)));
    Assert.That(failure!.Message, Does.Contain("0x16"));
  }

  private static double _Mean(RawImage image) => image.PixelData.Average(static value => (double)value);

  private static RawImage _Flat(int width, int height, byte value) {
    var pixels = new byte[width * height * 3];
    Array.Fill(pixels, value);
    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static RawImage _GreyscaleDetail(int width, int height) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var value = (byte)((x * 11 + y * 7) & 0xFF);
        var at = ((y * width) + x) * 3;
        pixels[at] = value;
        pixels[at + 1] = value;
        pixels[at + 2] = value;
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static MediaStreamInfo _Requested(
    int width,
    int height,
    int index = 0,
    MediaStreamKind kind = MediaStreamKind.Video,
    string? codec = null) => new() {
    Index = index,
    Kind = kind,
    Codec = codec == null ? CodecTag.None : CodecTag.FromCharacters(codec),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };
}
