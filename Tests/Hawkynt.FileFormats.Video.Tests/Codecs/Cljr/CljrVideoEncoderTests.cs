using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The CLJR encoder, measured against the package's own decoder of the same packets. The coding is
/// lossy, so what is asserted is not identity but the two facts a lossy coding can promise: the same
/// picture always codes to the same bytes, and every sample the decoder hands back is exactly the
/// quantiser's own rounding of the source — no luma sample more than 6 from where it started, no
/// chroma sample more than 4, the bounds measured over every one of the 256 values of each.
/// </summary>
[TestFixture]
public class CljrVideoEncoderTests {

  private static readonly CodecTag _Cljr = CodecTag.FromCharacters("CLJR");

  /// <summary>The reference quantiser's own bound, luma: five bits widened by replicating the top three.</summary>
  private const int _LUMA_TOLERANCE = 6;

  /// <summary>The reference quantiser's own bound, chroma: six bits widened by two zero bits.</summary>
  private const int _CHROMA_TOLERANCE = 4;

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  /// <summary>What the decoder gives back for a source sample: the reference's quantiser with its fixed offset of two, then the decoder's own widening.</summary>
  private static byte _ExpectedLuma(byte value) {
    var quantised = (249 * (value + 2)) >> 11;
    return (byte)((quantised << 3) | (quantised >> 2));
  }

  private static byte _ExpectedChroma(byte value) => (byte)(((253 * (value + 2)) >> 10) << 2);

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

  private static CljrVideoDecoder _Decoder(MediaStreamInfo described) {
    Assert.That(CljrVideoDecoder.Accepts(described), Is.True);
    var decoder = VideoFormatRegistry.CreateDecoder(described);
    Assert.That(decoder, Is.TypeOf<CljrVideoDecoder>());
    return (CljrVideoDecoder)decoder;
  }

  private static int _MaxDelta(byte[] source, byte[] decoded)
    => source.Zip(decoded, static (a, b) => Math.Abs(a - b)).Max();

  // ============================================================================================
  // DescribeStream
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void DescribesACljrStreamTheDecoderAccepts() {
    var described = CljrVideoEncoder.Create(_Stream(64, 33)).DescribeStream();

    Assert.Multiple(() => {
      Assert.That(CljrVideoEncoder.Codec, Is.EqualTo(_Cljr));
      Assert.That(described.Codec, Is.EqualTo(_Cljr));
      Assert.That(described.Handler, Is.EqualTo(_Cljr));
      Assert.That(described.Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(described.Width, Is.EqualTo(64));
      Assert.That(described.Height, Is.EqualTo(33));
      Assert.That(described.BitsPerPixel, Is.EqualTo(8));
      Assert.That(described.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(described.CodecPrivateData.IsEmpty, Is.True);
      Assert.That(CljrVideoDecoder.Accepts(described), Is.True);
    });
  }

  // ============================================================================================
  // The quantiser
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void EverySampleValueComesBackWithinTheQuantisersOwnBound() {
    var luma = Enumerable.Range(0, 256).Select(static v => (byte)v).ToArray();
    Assert.Multiple(() => {
      Assert.That(luma.Select(static v => Math.Abs(_ExpectedLuma(v) - v)), Is.All.LessThanOrEqualTo(_LUMA_TOLERANCE));
      Assert.That(luma.Select(static v => Math.Abs(_ExpectedChroma(v) - v)), Is.All.LessThanOrEqualTo(_CHROMA_TOLERANCE));
      Assert.That(luma.Max(static v => Math.Abs(_ExpectedLuma(v) - v)), Is.EqualTo(_LUMA_TOLERANCE));
      Assert.That(luma.Max(static v => Math.Abs(_ExpectedChroma(v) - v)), Is.EqualTo(_CHROMA_TOLERANCE));
    });
  }

  // ============================================================================================
  // Round trips through the registry's decoder
  // ============================================================================================

  [TestCase(4, 1, 1)]
  [TestCase(12, 5, 2)]
  [TestCase(64, 33, 3)]
  [TestCase(100, 7, 4)]
  [Category("Unit")]
  public void PlanesComeBackAsTheQuantisersRoundingOfTheSourceAndWithinItsBound(int width, int height, int seed) {
    var (frame, luma, cb, cr) = _RandomPicture(width, height, seed);
    var encoder = CljrVideoEncoder.Create(_Stream(width, height));

    Assert.That(encoder.TryEncode(frame, seed, out var packet), Is.True);
    Assert.That(packet.Data.Length, Is.EqualTo(width * height));

    var decoder = _Decoder(encoder.DescribeStream());
    var (decodedLuma, decodedCb, decodedCr) = decoder.DecodePlanes(packet.Data.Span);
    Assert.Multiple(() => {
      Assert.That(decodedLuma, Is.EqualTo(luma.Select(_ExpectedLuma).ToArray()));
      Assert.That(decodedCb, Is.EqualTo(cb.Select(_ExpectedChroma).ToArray()));
      Assert.That(decodedCr, Is.EqualTo(cr.Select(_ExpectedChroma).ToArray()));
      Assert.That(_MaxDelta(luma, decodedLuma), Is.LessThanOrEqualTo(_LUMA_TOLERANCE));
      Assert.That(_MaxDelta(cb, decodedCb), Is.LessThanOrEqualTo(_CHROMA_TOLERANCE));
      Assert.That(_MaxDelta(cr, decodedCr), Is.LessThanOrEqualTo(_CHROMA_TOLERANCE));
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
  public void EncodingTheSamePictureTwiceGivesTheSameBytes() {
    var (frame, _, _, _) = _RandomPicture(16, 4, 5);
    var encoder = CljrVideoEncoder.Create(_Stream(16, 4));

    Assert.That(encoder.TryEncode(frame, 0, out var first), Is.True);
    Assert.That(encoder.TryEncode(frame, 1, out var second), Is.True);
    Assert.That(second.Data.ToArray(), Is.EqualTo(first.Data.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void PacketsMuxIntoAnAviTheReaderHandsBackToTheSameDecoder() {
    var (frame, luma, cb, cr) = _RandomPicture(24, 5, 7);
    var encoder = CljrVideoEncoder.Create(_Stream(24, 5));
    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);

    var avi = VideoIO.Mux<AviWriter>([encoder.DescribeStream()], [packet]);
    var container = AviContainer.FromBytes(avi);
    var stream = AviContainer.Streams(container).Single();
    var decoder = _Decoder(stream);
    var (decodedLuma, decodedCb, decodedCr) = decoder.DecodePlanes(AviContainer.ReadPackets(container).Single().Data.Span);
    Assert.Multiple(() => {
      Assert.That(stream.BitsPerPixel, Is.EqualTo(8));
      Assert.That(decodedLuma, Is.EqualTo(luma.Select(_ExpectedLuma).ToArray()));
      Assert.That(decodedCb, Is.EqualTo(cb.Select(_ExpectedChroma).ToArray()));
      Assert.That(decodedCr, Is.EqualTo(cr.Select(_ExpectedChroma).ToArray()));
    });
  }

  // ============================================================================================
  // The word
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void OneGroupIsOneBigEndianWordWithTheLumaColumnsReversed() {
    byte[] luma = [0, 255, 16, 235];
    byte[] cb = [128];
    byte[] cr = [255];
    var encoder = CljrVideoEncoder.Create(_Stream(4, 1));

    var data = encoder.EncodePlanes(luma, cb, cr);
    var word = BinaryPrimitives.ReadUInt32BigEndian(data);
    Assert.Multiple(() => {
      Assert.That(data, Has.Length.EqualTo(4));
      Assert.That((word >> 12) & 0x1F, Is.EqualTo((249 * 2) >> 11));
      Assert.That((word >> 17) & 0x1F, Is.EqualTo((249 * 257) >> 11));
      Assert.That((word >> 22) & 0x1F, Is.EqualTo((249 * 18) >> 11));
      Assert.That((word >> 27) & 0x1F, Is.EqualTo((249 * 237) >> 11));
      Assert.That((word >> 6) & 0x3F, Is.EqualTo((253 * 130) >> 10));
      Assert.That(word & 0x3F, Is.EqualTo((253 * 257) >> 10));
    });
  }

  [Test]
  [Category("Unit")]
  public void RowsRunTopToBottom() {
    byte[] luma = [0, 0, 0, 0, 255, 255, 255, 255];
    byte[] cb = [0, 0];
    byte[] cr = [0, 0];
    var encoder = CljrVideoEncoder.Create(_Stream(4, 2));

    var data = encoder.EncodePlanes(luma, cb, cr);
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(data) >> 12, Is.Zero);
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)) >> 12, Is.EqualTo(0xFFFFFu));
    });
  }

  [Test]
  [Category("Unit")]
  public void APictureInAnotherFormatIsConvertedToStudioSwingPlanes() {
    var rgb = Enumerable.Repeat(new byte[] { 255, 255, 255 }, 4).SelectMany(static p => p).ToArray();
    var frame = new RawImage { Width = 4, Height = 1, Format = PixelFormat.Rgb24, PixelData = rgb };
    var encoder = CljrVideoEncoder.Create(_Stream(4, 1));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);
    var (luma, cb, cr) = _Decoder(encoder.DescribeStream()).DecodePlanes(packet.Data.Span);
    Assert.Multiple(() => {
      Assert.That(luma, Is.All.InRange(235 - _LUMA_TOLERANCE - 1, 235 + _LUMA_TOLERANCE + 1));
      Assert.That(cb, Is.All.InRange(128 - _CHROMA_TOLERANCE - 1, 128 + _CHROMA_TOLERANCE + 1));
      Assert.That(cr, Is.All.InRange(128 - _CHROMA_TOLERANCE - 1, 128 + _CHROMA_TOLERANCE + 1));
    });
  }

  // ============================================================================================
  // Packet bookkeeping
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void EveryPacketIsAKeyFrameCarryingItsTimestamp() {
    var (frame, _, _, _) = _RandomPicture(4, 1, 8);
    var encoder = CljrVideoEncoder.Create(_Stream(4, 1));

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
  public void RefusesAWidthThatIsNotAWholeNumberOfFourPixelGroups() {
    var failure = Assert.Throws<NotSupportedException>(() => CljrVideoEncoder.Create(_Stream(6, 4)));
    Assert.That(failure!.Message, Does.Contain("6"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAGeometryChangeMidStream() {
    var encoder = CljrVideoEncoder.Create(_Stream(4, 2));
    var (wrong, _, _, _) = _RandomPicture(4, 1, 9);

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(wrong, 0, out _));
    Assert.That(failure!.Message, Does.Contain("4x1"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixelsAndAStreamThatIsNotVideo() {
    Assert.Throws<InvalidDataException>(() => CljrVideoEncoder.Create(_Stream(0, 4)));
    Assert.Throws<NotSupportedException>(() => CljrVideoEncoder.Create(new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Width = 4, Height = 1 }));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureShortOfItsOwnPixelData() {
    var encoder = CljrVideoEncoder.Create(_Stream(4, 1));
    var frame = new RawImage { Width = 4, Height = 1, Format = PixelFormat.Yuv444P8, PixelData = new byte[5] };

    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(frame, 0, out _));
  }
}
