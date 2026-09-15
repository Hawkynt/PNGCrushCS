using System;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.CreativeYuv.Tests;

/// <summary>
/// The Creative YUV writer, on the real uncompressed <c>cyuv</c> packet shape: UYVY 4:2:2 stored
/// bottom row first.
/// </summary>
[TestFixture]
public class CreativeYuvVideoEncoderTests {

  private static readonly CodecTag _Cyuv = CodecTag.FromCharacters("cyuv");

  private static MediaStreamInfo _Stream(
    int width, int height, MediaStreamKind kind = MediaStreamKind.Video, int index = 2) => new() {
    Index = index,
    Kind = kind,
    Codec = _Cyuv,
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  /// <summary>
  /// Builds an eight-bit planar 4:2:2 picture and the same samples packed top-down as U, Y, V, Y.
  /// Chroma is already at the destination siting, so a writer has no resampling excuse for changing
  /// any sample.
  /// </summary>
  private static (RawImage Frame, byte[] Packed) _Picture(int width, int height, int seed) {
    var random = new Random(seed);
    var lumaSamples = width * height;
    var chromaWidth = width / 2;
    var chromaSamples = chromaWidth * height;
    var luma = new byte[lumaSamples];
    var cb = new byte[chromaSamples];
    var cr = new byte[chromaSamples];
    random.NextBytes(luma);
    random.NextBytes(cb);
    random.NextBytes(cr);

    var planes = new byte[lumaSamples + chromaSamples * 2];
    luma.CopyTo(planes, 0);
    cb.CopyTo(planes, lumaSamples);
    cr.CopyTo(planes, lumaSamples + chromaSamples);

    var packed = new byte[width * height * 2];
    for (var y = 0; y < height; ++y) {
      var lumaAt = y * width;
      var chromaAt = y * chromaWidth;
      var target = y * width * 2;
      for (var pair = 0; pair < chromaWidth; ++pair) {
        packed[target] = cb[chromaAt + pair];
        packed[target + 1] = luma[lumaAt + pair * 2];
        packed[target + 2] = cr[chromaAt + pair];
        packed[target + 3] = luma[lumaAt + pair * 2 + 1];
        target += 4;
      }
    }

    return (
      new RawImage {
        Width = width,
        Height = height,
        Format = PixelFormat.Yuv422P8,
        PixelData = planes,
      },
      packed);
  }

  // ============================================================================================
  // Registration and stream description
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheRegistryBuildsTheCreativeYuvEncoder() {
    var encoder = VideoFormatRegistry.CreateEncoder(_Stream(8, 3));

    Assert.That(encoder, Is.TypeOf<CreativeYuvVideoEncoder>());
  }

  [Test]
  [Category("Unit")]
  public void DescribesAStreamTheCreativeYuvDecoderAccepts() {
    var described = CreativeYuvVideoEncoder.Create(_Stream(8, 3)).DescribeStream();

    Assert.Multiple(() => {
      Assert.That(CreativeYuvVideoEncoder.Codec, Is.EqualTo(_Cyuv));
      Assert.That(described.Index, Is.EqualTo(2));
      Assert.That(described.Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(described.Codec, Is.EqualTo(_Cyuv));
      Assert.That(described.Handler, Is.EqualTo(_Cyuv));
      Assert.That(described.Width, Is.EqualTo(8));
      Assert.That(described.Height, Is.EqualTo(3));
      Assert.That(described.BitsPerPixel, Is.EqualTo(16));
      Assert.That(described.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(described.FrameRate, Is.EqualTo(new Rational(25, 1)));
      Assert.That(described.CodecPrivateData.IsEmpty, Is.True);
      Assert.That(CreativeYuvVideoDecoder.Accepts(described), Is.True);
    });
  }

  // ============================================================================================
  // The uncompressed packet shape
  // ============================================================================================

  [TestCase(4, 1, 1)]
  [TestCase(8, 3, 2)]
  [TestCase(64, 33, 3)]
  [Category("Unit")]
  public void Yuv422SamplesRoundTripExactlyThroughTheRawShape(int width, int height, int seed) {
    var (frame, topDown) = _Picture(width, height, seed);
    var encoder = CreativeYuvVideoEncoder.Create(_Stream(width, height));

    Assert.That(encoder.TryEncode(frame, seed, out var packet), Is.True);
    Assert.That(packet.Data.Length, Is.EqualTo(width * height * 2),
      "the encoder writes the raw UYVY shape, not the 48-byte differential-table shape");

    var decoder = CreativeYuvVideoDecoder.Create(encoder.DescribeStream());
    var decoded = decoder.DecodePackedSamples(packet.Data.Span);
    Assert.That(decoded, Is.EqualTo(topDown));
  }

  [Test]
  [Category("Unit")]
  public void PacketRowsAreStoredBottomFirst() {
    const int width = 4;
    const int height = 3;
    var (frame, topDown) = _Picture(width, height, 4);
    var encoder = CreativeYuvVideoEncoder.Create(_Stream(width, height));
    var stride = width * 2;

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);
    var data = packet.Data.ToArray();

    Assert.Multiple(() => {
      Assert.That(data[..stride], Is.EqualTo(topDown[^stride..]), "first coded row is the displayed bottom row");
      Assert.That(data[^stride..], Is.EqualTo(topDown[..stride]), "last coded row is the displayed top row");
    });
  }

  [Test]
  [Category("Unit")]
  public void BottomUpConversionRefusesTruncatedPackedSamples() {
    var encoder = CreativeYuvVideoEncoder.Create(_Stream(8, 2));

    var failure = Assert.Throws<InvalidDataException>(() => encoder.ToBottomUp(new byte[31]));
    Assert.That(failure!.Message, Does.Contain("needs 32 byte(s)"));
  }

  [Test]
  [Category("Unit")]
  public void RgbInputUsesTheSharedBt601YuvConversion() {
    var frame = new RawImage {
      Width = 4,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = Enumerable.Repeat(new byte[] { 255, 255, 255 }, 4).SelectMany(static pixel => pixel).ToArray(),
    };
    var encoder = CreativeYuvVideoEncoder.Create(_Stream(4, 1));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);
    var samples = CreativeYuvVideoDecoder.Create(encoder.DescribeStream()).DecodePackedSamples(packet.Data.Span);

    Assert.Multiple(() => {
      Assert.That(samples[1], Is.InRange(234, 236));
      Assert.That(samples[3], Is.InRange(234, 236));
      Assert.That(samples[0], Is.InRange(127, 129));
      Assert.That(samples[2], Is.InRange(127, 129));
    });
  }

  // ============================================================================================
  // Container interoperability inside the package
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void PacketsMuxIntoAnAviAndComeBackThroughTheCreativeYuvDecoder() {
    var (frame, topDown) = _Picture(8, 3, 5);
    // AVI numbers its streams densely from zero, so this one cannot use the fixture's index of 2.
    var encoder = CreativeYuvVideoEncoder.Create(_Stream(8, 3, index: 0));
    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);

    var avi = VideoIO.Mux<AviWriter>([encoder.DescribeStream()], [packet]);
    var container = AviContainer.FromBytes(avi);
    var stream = AviContainer.Streams(container).Single();
    var demuxed = AviContainer.ReadPackets(container).Single();

    Assert.Multiple(() => {
      Assert.That(stream.Codec, Is.EqualTo(_Cyuv));
      Assert.That(stream.Handler, Is.EqualTo(_Cyuv));
      Assert.That(stream.BitsPerPixel, Is.EqualTo(16));
      Assert.That(demuxed.Data.Length, Is.EqualTo(8 * 3 * 2));
    });

    var decoder = VideoFormatRegistry.CreateDecoder(stream);
    Assert.That(decoder, Is.TypeOf<CreativeYuvVideoDecoder>());
    Assert.That(((CreativeYuvVideoDecoder)decoder).DecodePackedSamples(demuxed.Data.Span), Is.EqualTo(topDown));
  }

  // ============================================================================================
  // Packet bookkeeping and refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void EveryPacketIsAKeyFrameCarryingItsTimestamp() {
    var (frame, _) = _Picture(4, 1, 6);
    var encoder = CreativeYuvVideoEncoder.Create(_Stream(4, 1));

    Assert.That(encoder.TryEncode(frame, 37, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(packet.StreamIndex, Is.EqualTo(2));
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(37));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(37));
      Assert.That(packet.Duration, Is.EqualTo(1));
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(((IVideoPacketEncoder)encoder).Flush(), Is.Empty);
    });
  }

  [Test]
  [Category("Unit")]
  public void RefusesAWidthThatIsNotAWholeNumberOfFourPixelGroups() {
    var failure = Assert.Throws<NotSupportedException>(() => CreativeYuvVideoEncoder.Create(_Stream(6, 2)));
    Assert.That(failure!.Message, Does.Contain("width of 6"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesANonVideoStreamAndAPictureWithNoPixels() {
    Assert.Throws<NotSupportedException>(() => CreativeYuvVideoEncoder.Create(_Stream(4, 1, MediaStreamKind.Audio)));
    Assert.Throws<InvalidDataException>(() => CreativeYuvVideoEncoder.Create(_Stream(0, 1)));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAGeometryChangeMidStream() {
    var encoder = CreativeYuvVideoEncoder.Create(_Stream(8, 2));
    var (wrong, _) = _Picture(8, 1, 7);

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(wrong, 0, out _));
    Assert.That(failure!.Message, Does.Contain("8x1"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureShortOfItsOwnPixelData() {
    var encoder = CreativeYuvVideoEncoder.Create(_Stream(4, 1));
    var frame = new RawImage {
      Width = 4,
      Height = 1,
      Format = PixelFormat.Yuv422P8,
      PixelData = new byte[7],
    };

    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(frame, 0, out _));
  }
}
