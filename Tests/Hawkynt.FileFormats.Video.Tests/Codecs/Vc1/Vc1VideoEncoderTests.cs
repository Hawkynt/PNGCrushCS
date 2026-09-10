using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Vc1.Tests;

/// <summary>The deliberately small VC-1 writer: Main-profile, progressive, all-intra and DC-only.</summary>
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
      Assert.That(sequence.MaxBFrames, Is.Zero);
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
  public void EveryPictureCanBeDecodedWithoutTheOneBeforeIt() {
    var encoder = Vc1VideoEncoder.Create(_Requested(16, 16));
    Assert.That(encoder.TryEncode(_Flat(16, 16, 32), 0, out _), Is.True);
    Assert.That(encoder.TryEncode(_Flat(16, 16, 192), 1, out var second), Is.True);

    var freshDecoder = Vc1VideoDecoder.Create(encoder.DescribeStream());
    Assert.That(freshDecoder.TryDecode(second, out var decoded), Is.True);
    Assert.Multiple(() => {
      Assert.That(second.IsKeyFrame, Is.True);
      Assert.That(decoded.Width, Is.EqualTo(16));
      Assert.That(decoded.Height, Is.EqualTo(16));
    });
  }

  [Test]
  [Category("Unit")]
  public void ASkippedPictureRepeatsThePreviousDecodedPicture() {
    var encoder = Vc1VideoEncoder.Create(_Requested(16, 16));
    Assert.That(encoder.TryEncode(_Flat(16, 16, 128), 0, out var coded), Is.True);

    var decoder = Vc1VideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(coded, out var first), Is.True);
    var expected = (byte[])first.PixelData.Clone();

    // Returned images belong to the caller. Mutating one must not alter the decoder's retained picture.
    Array.Fill(first.PixelData, (byte)0);
    Assert.That(decoder.TryDecode(new(0, new byte[1]), out var repeated), Is.True);

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

  private static RawImage _Flat(int width, int height, byte value) {
    var pixels = new byte[width * height * 3];
    Array.Fill(pixels, value);
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
