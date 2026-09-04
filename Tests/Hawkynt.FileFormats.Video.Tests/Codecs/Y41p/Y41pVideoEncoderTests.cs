using System;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The y41p encoder, measured against the package's own decoder of the same packets: the eight-bit
/// planes have to come back from <see cref="Y41pVideoDecoder.DecodePlanes"/> sample for sample, and
/// the bytes themselves have to be the ones the decoder documents, bottom row first.
/// </summary>
[TestFixture]
public class Y41pVideoEncoderTests {

  private static readonly CodecTag _Y41p = CodecTag.FromCharacters("Y41P");

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  /// <summary>Pseudo-random 4:1:1 planes and the same picture spread out as 4:4:4, each chroma pair repeated across its four columns.</summary>
  private static (RawImage Frame, byte[] Luma, byte[] Cb, byte[] Cr) _RandomPicture(int width, int height, int seed) {
    var random = new Random(seed);
    var chromaWidth = width / 4;
    var luma = new byte[width * height];
    var cb = new byte[chromaWidth * height];
    var cr = new byte[chromaWidth * height];
    random.NextBytes(luma);
    random.NextBytes(cb);
    random.NextBytes(cr);

    var data = new byte[width * height * 3];
    luma.CopyTo(data, 0);
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        data[width * height + y * width + x] = cb[y * chromaWidth + x / 4];
        data[2 * width * height + y * width + x] = cr[y * chromaWidth + x / 4];
      }

    var frame = new RawImage { Width = width, Height = height, Format = PixelFormat.Yuv444P8, PixelData = data };
    return (frame, luma, cb, cr);
  }

  private static Y41pVideoDecoder _Decoder(MediaStreamInfo described) {
    Assert.That(Y41pVideoDecoder.Accepts(described), Is.True);
    var decoder = VideoFormatRegistry.CreateDecoder(described);
    Assert.That(decoder, Is.TypeOf<Y41pVideoDecoder>());
    return (Y41pVideoDecoder)decoder;
  }

  // ============================================================================================
  // DescribeStream
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void DescribesAY41pStreamTheDecoderAccepts() {
    var described = Y41pVideoEncoder.Create(_Stream(64, 33)).DescribeStream();

    Assert.Multiple(() => {
      Assert.That(Y41pVideoEncoder.Codec, Is.EqualTo(_Y41p));
      Assert.That(described.Codec, Is.EqualTo(_Y41p));
      Assert.That(described.Handler, Is.EqualTo(_Y41p));
      Assert.That(described.Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(described.Width, Is.EqualTo(64));
      Assert.That(described.Height, Is.EqualTo(33));
      Assert.That(described.BitsPerPixel, Is.EqualTo(12));
      Assert.That(described.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(described.CodecPrivateData.IsEmpty, Is.True);
      Assert.That(Y41pVideoDecoder.Accepts(described), Is.True);
    });
  }

  // ============================================================================================
  // Round trips through the registry's decoder
  // ============================================================================================

  [TestCase(8, 1, 1)]
  [TestCase(16, 3, 2)]
  [TestCase(64, 8, 3)]
  [TestCase(96, 40, 4)]
  [TestCase(128, 33, 5)]
  [Category("Unit")]
  public void PlanesComeBackIdentical(int width, int height, int seed) {
    var (frame, luma, cb, cr) = _RandomPicture(width, height, seed);
    var encoder = Y41pVideoEncoder.Create(_Stream(width, height));

    Assert.That(encoder.TryEncode(frame, seed, out var packet), Is.True);
    Assert.That(packet.Data.Length, Is.EqualTo(width * 3 / 2 * height));

    var decoder = _Decoder(encoder.DescribeStream());
    var (decodedLuma, decodedCb, decodedCr) = decoder.DecodePlanes(packet.Data.Span);
    Assert.Multiple(() => {
      Assert.That(decodedLuma, Is.EqualTo(luma));
      Assert.That(decodedCb, Is.EqualTo(cb));
      Assert.That(decodedCr, Is.EqualTo(cr));
    });

    Assert.That(decoder.TryDecode(packet, out var picture), Is.True);
    Assert.Multiple(() => {
      Assert.That(picture.Width, Is.EqualTo(width));
      Assert.That(picture.Height, Is.EqualTo(height));
      Assert.That(picture.Format, Is.EqualTo(PixelFormat.Rgb24));
    });
  }

  [Test]
  [Category("Unit")]
  public void AFourTwoTwoPictureSitedOnFourColumnsComesBackIdentical() {
    const int width = 16, height = 3;
    var (_, luma, cb, cr) = _RandomPicture(width, height, 6);
    var data = new byte[width * height + 2 * (width / 2) * height];
    luma.CopyTo(data, 0);
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width / 2; ++x) {
        data[width * height + y * (width / 2) + x] = cb[y * (width / 4) + x / 2];
        data[width * height + (width / 2) * height + y * (width / 2) + x] = cr[y * (width / 4) + x / 2];
      }
    var frame = new RawImage { Width = width, Height = height, Format = PixelFormat.Yuv422P8, PixelData = data };
    var encoder = Y41pVideoEncoder.Create(_Stream(width, height));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);
    var (decodedLuma, decodedCb, decodedCr) = _Decoder(encoder.DescribeStream()).DecodePlanes(packet.Data.Span);
    Assert.Multiple(() => {
      Assert.That(decodedLuma, Is.EqualTo(luma));
      Assert.That(decodedCb, Is.EqualTo(cb));
      Assert.That(decodedCr, Is.EqualTo(cr));
    });
  }

  [Test]
  [Category("Unit")]
  public void ChromaCarriedAtMoreThanOnePairPerFourColumnsIsAveragedToNearest() {
    byte[] luma = new byte[8];
    var data = new byte[8 * 3];
    byte[] cb = [10, 20, 30, 40, 1, 2, 2, 2];
    byte[] cr = [255, 255, 255, 254, 0, 0, 0, 1];
    cb.CopyTo(data, 8);
    cr.CopyTo(data, 16);
    var frame = new RawImage { Width = 8, Height = 1, Format = PixelFormat.Yuv444P8, PixelData = data };
    var encoder = Y41pVideoEncoder.Create(_Stream(8, 1));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);
    var (_, decodedCb, decodedCr) = _Decoder(encoder.DescribeStream()).DecodePlanes(packet.Data.Span);
    Assert.Multiple(() => {
      Assert.That(decodedCb, Is.EqualTo(new byte[] { 25, 2 }));
      Assert.That(decodedCr, Is.EqualTo(new byte[] { 255, 0 }));
    });
    Assert.That(luma, Has.Length.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void PacketsMuxIntoAnAviTheReaderHandsBackToTheSameDecoder() {
    var (frame, luma, cb, cr) = _RandomPicture(24, 5, 7);
    var encoder = Y41pVideoEncoder.Create(_Stream(24, 5));
    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);

    var avi = VideoIO.Mux<AviWriter>([encoder.DescribeStream()], [packet]);
    var container = AviContainer.FromBytes(avi);
    var stream = AviContainer.Streams(container).Single();
    var decoder = _Decoder(stream);
    var (decodedLuma, decodedCb, decodedCr) = decoder.DecodePlanes(AviContainer.ReadPackets(container).Single().Data.Span);
    Assert.Multiple(() => {
      Assert.That(stream.BitsPerPixel, Is.EqualTo(12));
      Assert.That(decodedLuma, Is.EqualTo(luma));
      Assert.That(decodedCb, Is.EqualTo(cb));
      Assert.That(decodedCr, Is.EqualTo(cr));
    });
  }

  // ============================================================================================
  // The bytes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void OneGroupIsTwelveBytesInTheDecodersOrder() {
    byte[] luma = [1, 2, 3, 4, 5, 6, 7, 8];
    byte[] cb = [100, 101];
    byte[] cr = [200, 201];
    var encoder = Y41pVideoEncoder.Create(_Stream(8, 1));

    var data = encoder.EncodePlanes(luma, cb, cr);
    Assert.That(data, Is.EqualTo(new byte[] { 100, 1, 200, 2, 101, 3, 201, 4, 5, 6, 7, 8 }));
  }

  [Test]
  [Category("Unit")]
  public void RowsAreCodedBottomRowFirst() {
    var luma = Enumerable.Range(0, 16).Select(static i => (byte)i).ToArray();
    var cb = new byte[] { 10, 11, 12, 13 };
    var cr = new byte[] { 20, 21, 22, 23 };
    var encoder = Y41pVideoEncoder.Create(_Stream(8, 2));

    var data = encoder.EncodePlanes(luma, cb, cr);
    Assert.Multiple(() => {
      Assert.That(data[..12], Is.EqualTo(new byte[] { 12, 8, 22, 9, 13, 10, 23, 11, 12, 13, 14, 15 }));
      Assert.That(data[12..], Is.EqualTo(new byte[] { 10, 0, 20, 1, 11, 2, 21, 3, 4, 5, 6, 7 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void APictureInAnotherFormatIsConvertedToStudioSwingPlanes() {
    var rgb = Enumerable.Repeat(new byte[] { 255, 255, 255 }, 8).SelectMany(static p => p).ToArray();
    var frame = new RawImage { Width = 8, Height = 1, Format = PixelFormat.Rgb24, PixelData = rgb };
    var encoder = Y41pVideoEncoder.Create(_Stream(8, 1));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);
    var (luma, cb, cr) = _Decoder(encoder.DescribeStream()).DecodePlanes(packet.Data.Span);
    Assert.Multiple(() => {
      Assert.That(luma, Is.All.InRange(234, 236));
      Assert.That(cb, Is.All.InRange(127, 129));
      Assert.That(cr, Is.All.InRange(127, 129));
    });
  }

  // ============================================================================================
  // Packet bookkeeping
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void EveryPacketIsAKeyFrameCarryingItsTimestamp() {
    var (frame, _, _, _) = _RandomPicture(8, 1, 8);
    var encoder = Y41pVideoEncoder.Create(_Stream(8, 1));

    Assert.That(encoder.TryEncode(frame, 37, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(packet.StreamIndex, Is.Zero);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(37));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(37));
      Assert.That(packet.Duration, Is.EqualTo(1));
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(((IVideoPacketEncoder)encoder).Flush(), Is.Empty);
    });

    Assert.That(encoder.TryEncode(frame, null, out packet), Is.True);
    Assert.That(packet.PresentationTimestamp, Is.Null);
  }

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void RefusesAWidthThatIsNotAWholeNumberOfEightPixelGroups() {
    var failure = Assert.Throws<NotSupportedException>(() => Y41pVideoEncoder.Create(_Stream(12, 4)));
    Assert.That(failure!.Message, Does.Contain("12"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAGeometryChangeMidStream() {
    var encoder = Y41pVideoEncoder.Create(_Stream(8, 2));
    var (wrong, _, _, _) = _RandomPicture(8, 1, 9);

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(wrong, 0, out _));
    Assert.That(failure!.Message, Does.Contain("8x1"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixelsAndAStreamThatIsNotVideo() {
    Assert.Throws<InvalidDataException>(() => Y41pVideoEncoder.Create(_Stream(0, 4)));
    Assert.Throws<NotSupportedException>(() => Y41pVideoEncoder.Create(new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Width = 8, Height = 1 }));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureShortOfItsOwnPixelData() {
    var encoder = Y41pVideoEncoder.Create(_Stream(8, 1));
    var frame = new RawImage { Width = 8, Height = 1, Format = PixelFormat.Yuv444P8, PixelData = new byte[10] };

    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(frame, 0, out _));
  }
}
