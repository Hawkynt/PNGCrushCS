using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The v210 encoder, measured against the package's own decoder of the same packets: the ten-bit
/// planes have to come back from <see cref="V210VideoDecoder.DecodePlanes"/> sample for sample, and
/// the words themselves have to be the ones the decoder documents.
/// </summary>
[TestFixture]
public class V210VideoEncoderTests {

  private static readonly CodecTag _V210 = CodecTag.FromCharacters("v210");

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static int _Stride(int width) => ((width + 5) / 6 * 16 + 127) / 128 * 128;

  /// <summary>Pseudo-random ten-bit planes packed into the little-endian sixteen-bit slots of <see cref="PixelFormat.Yuv422P10"/>.</summary>
  private static (RawImage Frame, ushort[] Luma, ushort[] Cb, ushort[] Cr) _RandomPicture(int width, int height, int seed) {
    var random = new Random(seed);
    var chromaWidth = (width + 1) / 2;
    var luma = new ushort[width * height];
    var cb = new ushort[chromaWidth * height];
    var cr = new ushort[chromaWidth * height];
    for (var i = 0; i < luma.Length; ++i)
      luma[i] = (ushort)random.Next(1024);
    for (var i = 0; i < cb.Length; ++i) {
      cb[i] = (ushort)random.Next(1024);
      cr[i] = (ushort)random.Next(1024);
    }

    var data = new byte[(luma.Length + cb.Length + cr.Length) * 2];
    var offset = 0;
    foreach (var plane in new[] { luma, cb, cr })
      foreach (var sample in plane) {
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset), sample);
        offset += 2;
      }

    var frame = new RawImage { Width = width, Height = height, Format = PixelFormat.Yuv422P10, PixelData = data };
    return (frame, luma, cb, cr);
  }

  private static V210VideoDecoder _Decoder(MediaStreamInfo described) {
    Assert.That(V210VideoDecoder.Accepts(described), Is.True);
    var decoder = VideoFormatRegistry.CreateDecoder(described);
    Assert.That(decoder, Is.TypeOf<V210VideoDecoder>());
    return (V210VideoDecoder)decoder;
  }

  // ============================================================================================
  // DescribeStream
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void DescribesAV210StreamTheDecoderAccepts() {
    var described = V210VideoEncoder.Create(_Stream(22, 18)).DescribeStream();

    Assert.Multiple(() => {
      Assert.That(V210VideoEncoder.Codec, Is.EqualTo(_V210));
      Assert.That(described.Codec, Is.EqualTo(_V210));
      Assert.That(described.Handler, Is.EqualTo(_V210));
      Assert.That(described.Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(described.Width, Is.EqualTo(22));
      Assert.That(described.Height, Is.EqualTo(18));
      Assert.That(described.BitsPerPixel, Is.EqualTo(20));
      Assert.That(described.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(described.CodecPrivateData.IsEmpty, Is.True);
      Assert.That(V210VideoDecoder.Accepts(described), Is.True);
    });
  }

  // ============================================================================================
  // Round trips through the registry's decoder
  // ============================================================================================

  [TestCase(6, 1, 1)]
  [TestCase(7, 3, 2)]
  [TestCase(22, 18, 3)]
  [TestCase(48, 32, 4)]
  [TestCase(1, 5, 5)]
  [Category("Unit")]
  public void TenBitPlanesComeBackIdentical(int width, int height, int seed) {
    var (frame, luma, cb, cr) = _RandomPicture(width, height, seed);
    var encoder = V210VideoEncoder.Create(_Stream(width, height));

    Assert.That(encoder.TryEncode(frame, seed, out var packet), Is.True);
    Assert.That(packet.Data.Length, Is.EqualTo(_Stride(width) * height));

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
  public void EncodingTheSamePictureTwiceGivesTheSameBytes() {
    var (frame, _, _, _) = _RandomPicture(14, 4, 6);
    var encoder = V210VideoEncoder.Create(_Stream(14, 4));

    Assert.That(encoder.TryEncode(frame, 0, out var first), Is.True);
    Assert.That(encoder.TryEncode(frame, 1, out var second), Is.True);
    Assert.That(second.Data.ToArray(), Is.EqualTo(first.Data.ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void PacketsMuxIntoAnAviTheReaderHandsBackToTheSameDecoder() {
    var (frame, luma, cb, cr) = _RandomPicture(10, 3, 7);
    var encoder = V210VideoEncoder.Create(_Stream(10, 3));
    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);

    var avi = VideoIO.Mux<AviWriter>([encoder.DescribeStream()], [packet]);
    var container = AviContainer.FromBytes(avi);
    var stream = AviContainer.Streams(container).Single();
    var decoder = _Decoder(stream);
    var (decodedLuma, decodedCb, decodedCr) = decoder.DecodePlanes(AviContainer.ReadPackets(container).Single().Data.Span);
    Assert.Multiple(() => {
      Assert.That(stream.BitsPerPixel, Is.EqualTo(20));
      Assert.That(decodedLuma, Is.EqualTo(luma));
      Assert.That(decodedCb, Is.EqualTo(cb));
      Assert.That(decodedCr, Is.EqualTo(cr));
    });
  }

  // ============================================================================================
  // The words
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void OneGroupPacksSixLumaAndThreeChromaPairsIntoFourWords() {
    ushort[] luma = [1, 2, 3, 4, 5, 6];
    ushort[] cb = [100, 200, 300];
    ushort[] cr = [400, 500, 600];
    var encoder = V210VideoEncoder.Create(_Stream(6, 1));

    var data = encoder.EncodePlanes(luma, cb, cr);
    Assert.That(data, Has.Length.EqualTo(128));
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(data), Is.EqualTo(100u | (1u << 10) | (400u << 20)));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4)), Is.EqualTo(2u | (200u << 10) | (3u << 20)));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8)), Is.EqualTo(500u | (4u << 10) | (300u << 20)));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12)), Is.EqualTo(5u | (600u << 10) | (6u << 20)));
      Assert.That(data[16..], Is.All.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void APartialLastGroupWritesTheColumnsPastTheWidthAsZero() {
    ushort[] luma = [1023, 1023, 1023, 1023, 1023, 1023, 1023];
    ushort[] cb = [1023, 1023, 1023, 1023];
    ushort[] cr = [1023, 1023, 1023, 1023];
    var encoder = V210VideoEncoder.Create(_Stream(7, 1));

    var data = encoder.EncodePlanes(luma, cb, cr);
    // The seventh column is luma 0 with the fourth chroma pair; luma 1 and later, and the pairs after, are zero.
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(16)), Is.EqualTo(1023u | (1023u << 10) | (1023u << 20)));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(20)), Is.Zero);
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(24)), Is.Zero);
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(28)), Is.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void APictureInAnotherFormatIsConvertedToStudioSwingTenBitPlanes() {
    var rgb = Enumerable.Repeat(new byte[] { 255, 255, 255 }, 6).SelectMany(static p => p).ToArray();
    var frame = new RawImage { Width = 6, Height = 1, Format = PixelFormat.Rgb24, PixelData = rgb };
    var encoder = V210VideoEncoder.Create(_Stream(6, 1));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);
    var (luma, cb, cr) = _Decoder(encoder.DescribeStream()).DecodePlanes(packet.Data.Span);
    Assert.Multiple(() => {
      // White at studio swing is luma 235 and chroma 128 at eight bits; four times that at ten.
      Assert.That(luma, Is.All.InRange(235 * 4 - 4, 235 * 4 + 4));
      Assert.That(cb, Is.All.InRange(128 * 4 - 4, 128 * 4 + 4));
      Assert.That(cr, Is.All.InRange(128 * 4 - 4, 128 * 4 + 4));
    });
  }

  // ============================================================================================
  // Packet bookkeeping
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void EveryPacketIsAKeyFrameCarryingItsTimestamp() {
    var (frame, _, _, _) = _RandomPicture(6, 1, 8);
    var encoder = V210VideoEncoder.Create(_Stream(6, 1));

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
  public void RefusesAGeometryChangeMidStream() {
    var encoder = V210VideoEncoder.Create(_Stream(6, 2));
    var (wrong, _, _, _) = _RandomPicture(6, 1, 9);

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(wrong, 0, out _));
    Assert.That(failure!.Message, Does.Contain("6x1"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesASampleThatDoesNotFitTenBits() {
    var (frame, _, _, _) = _RandomPicture(6, 1, 10);
    BinaryPrimitives.WriteUInt16LittleEndian(frame.PixelData.AsSpan(2), 1024);
    var encoder = V210VideoEncoder.Create(_Stream(6, 1));

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(frame, 0, out _));
    Assert.That(failure!.Message, Does.Contain("1024"));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureWithNoPixelsAndAStreamThatIsNotVideo() {
    Assert.Throws<InvalidDataException>(() => V210VideoEncoder.Create(_Stream(0, 4)));
    Assert.Throws<NotSupportedException>(() => V210VideoEncoder.Create(new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Width = 6, Height = 1 }));
  }

  [Test]
  [Category("Unit")]
  public void RefusesAPictureShortOfItsOwnPixelData() {
    var encoder = V210VideoEncoder.Create(_Stream(6, 1));
    var frame = new RawImage { Width = 6, Height = 1, Format = PixelFormat.Yuv422P10, PixelData = new byte[10] };

    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(frame, 0, out _));
  }
}
