using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The v210x encoder checked at the ten-bit plane boundary and against independently calculated words.
/// </summary>
[TestFixture]
public sealed class V210XVideoEncoderTests {

  private static MediaStreamInfo _Stream(int width, int height, string? codecId = null, CodecTag? codec = null) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = codec ?? CodecTag.None,
    CodecId = codecId,
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static (RawImage Frame, ushort[] Luma, ushort[] Cb, ushort[] Cr) _RandomPicture(int width, int height, int seed) {
    var random = new Random(seed);
    var luma = new ushort[width * height];
    var cb = new ushort[width / 2 * height];
    var cr = new ushort[width / 2 * height];
    for (var i = 0; i < luma.Length; ++i)
      luma[i] = (ushort)random.Next(1024);
    for (var i = 0; i < cb.Length; ++i) {
      cb[i] = (ushort)random.Next(1024);
      cr[i] = (ushort)random.Next(1024);
    }

    var bytes = new byte[(luma.Length + cb.Length + cr.Length) * 2];
    var offset = 0;
    foreach (var plane in new[] { luma, cb, cr })
      foreach (var sample in plane) {
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), sample);
        offset += 2;
      }

    return (new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Yuv422P10,
      PixelData = bytes,
    }, luma, cb, cr);
  }

  private static int _RawPacketSize(int width, int height) => ((width + 47) / 48 * 48) * height * 8 / 3;

  [Test]
  [Category("Unit")]
  public void DescribesTheTextNamedCodecWithoutInventingAFourCc() {
    var encoder = V210XVideoEncoder.Create(_Stream(6, 2));
    var described = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(V210XVideoEncoder.Codec, Is.EqualTo(CodecTag.None));
      Assert.That(described.Codec, Is.EqualTo(CodecTag.None));
      Assert.That(described.Handler, Is.EqualTo(CodecTag.None));
      Assert.That(described.CodecId, Is.EqualTo("v210x"));
      Assert.That(described.Width, Is.EqualTo(6));
      Assert.That(described.Height, Is.EqualTo(2));
      Assert.That(described.BitsPerPixel, Is.EqualTo(20));
      Assert.That(described.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(V210XVideoDecoder.Accepts(described), Is.True);
    });
  }

  [Test]
  [Category("Unit")]
  public void RegistryRoutesTextNamedV210XPastTheGenericBiRgbEncoder() {
    var requested = _Stream(6, 2, "v210x");

    Assert.Multiple(() => {
      Assert.That(RawVideoEncoder.Accepts(requested), Is.False);
      Assert.That(V210XVideoEncoder.Accepts(requested), Is.True);
      Assert.That(VideoFormatRegistry.CanEncode(requested), Is.True);
      Assert.That(VideoFormatRegistry.CanDecode(requested), Is.True);
      Assert.That(VideoFormatRegistry.CreateEncoder(requested), Is.TypeOf<V210XVideoEncoder>());
      Assert.That(VideoFormatRegistry.CreateDecoder(requested), Is.TypeOf<V210XVideoDecoder>());
    });
  }

  [TestCase(2, 3, 1)]
  [TestCase(4, 5, 2)]
  [TestCase(6, 1, 3)]
  [TestCase(10, 7, 4)]
  [TestCase(48, 2, 5)]
  [Category("Unit")]
  public void TenBitPlanesRoundTripExactlyAcrossWordAndRowBoundaries(int width, int height, int seed) {
    var (frame, luma, cb, cr) = _RandomPicture(width, height, seed);
    var encoder = V210XVideoEncoder.Create(_Stream(width, height));

    Assert.That(encoder.TryEncode(frame, seed, out var packet), Is.True);
    Assert.That(packet.Data.Length, Is.EqualTo(_RawPacketSize(width, height)));

    var decoder = V210XVideoDecoder.Create(encoder.DescribeStream());
    var (decodedLuma, decodedCb, decodedCr) = decoder.DecodePlanes(packet.Data.Span);
    Assert.Multiple(() => {
      Assert.That(decodedLuma, Is.EqualTo(luma));
      Assert.That(decodedCb, Is.EqualTo(cb));
      Assert.That(decodedCr, Is.EqualTo(cr));
    });
  }

  [Test]
  [Category("Unit")]
  public void SixPixelsPackIntoTheFourBigEndianWordsTheReferenceLayoutDefines() {
    ushort[] luma = [1, 2, 3, 4, 5, 6];
    ushort[] cb = [100, 200, 300];
    ushort[] cr = [400, 500, 600];
    var encoder = V210XVideoEncoder.Create(_Stream(6, 1));

    var data = encoder.EncodePlanes(luma, cb, cr);

    Assert.Multiple(() => {
      Assert.That(data, Has.Length.EqualTo(128));
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(data), Is.EqualTo((100u << 22) | (1u << 12) | (400u << 2)));
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)), Is.EqualTo((2u << 22) | (200u << 12) | (3u << 2)));
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8)), Is.EqualTo((500u << 22) | (4u << 12) | (300u << 2)));
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12)), Is.EqualTo((5u << 22) | (600u << 12) | (6u << 2)));
      Assert.That(data[16..], Is.All.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void APartialFinalWordIsZeroFilledAndTheFrameGetsRawYuv10TailPadding() {
    ushort[] luma = [100, 200];
    ushort[] cb = [10];
    ushort[] cr = [20];
    var encoder = V210XVideoEncoder.Create(_Stream(2, 1));

    var data = encoder.EncodePlanes(luma, cb, cr);

    Assert.Multiple(() => {
      Assert.That(data, Has.Length.EqualTo(128));
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(data), Is.EqualTo((10u << 22) | (100u << 12) | (20u << 2)));
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)), Is.EqualTo(200u << 22));
      Assert.That(data[8..], Is.All.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void EveryPacketIsAnIndependentKeyFrameCarryingItsTimestamp() {
    var (frame, _, _, _) = _RandomPicture(6, 1, 6);
    var encoder = V210XVideoEncoder.Create(_Stream(6, 1));

    Assert.That(encoder.TryEncode(frame, 37, out var packet), Is.True);
    Assert.Multiple(() => {
      Assert.That(packet.StreamIndex, Is.Zero);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(37));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(37));
      Assert.That(packet.Duration, Is.EqualTo(1));
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(((IVideoPacketEncoder)encoder).Flush(), Is.Empty);
    });
  }

  [Test]
  [Category("Unit")]
  public void RefusesOddWidthWrongCodecGeometryChangesAndOutOfRangeSamples() {
    Assert.Throws<InvalidDataException>(() => V210XVideoEncoder.Create(_Stream(3, 1)));
    Assert.Throws<NotSupportedException>(() => V210XVideoEncoder.Create(_Stream(6, 1, "not-v210x")));
    Assert.Throws<NotSupportedException>(() => V210XVideoEncoder.Create(_Stream(6, 1, codec: CodecTag.FromCharacters("v210"))));

    var encoder = V210XVideoEncoder.Create(_Stream(6, 1));
    var (wrongGeometry, _, _, _) = _RandomPicture(6, 2, 7);
    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(wrongGeometry, 0, out _));

    ushort[] luma = [1024, 0, 0, 0, 0, 0];
    ushort[] cb = [0, 0, 0];
    ushort[] cr = [0, 0, 0];
    var failure = Assert.Throws<InvalidDataException>(() => encoder.EncodePlanes(luma, cb, cr));
    Assert.That(failure!.Message, Does.Contain("1024"));
  }

  [Test]
  [Category("Unit")]
  public void ConvertsRgbInputToTenBit422BeforePacking() {
    var frame = new RawImage {
      Width = 6,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [
        255, 255, 255,
        255, 255, 255,
        255, 255, 255,
        255, 255, 255,
        255, 255, 255,
        255, 255, 255,
      ],
    };
    var encoder = V210XVideoEncoder.Create(_Stream(6, 1));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);
    var decoder = V210XVideoDecoder.Create(encoder.DescribeStream());
    var (luma, cb, cr) = decoder.DecodePlanes(packet.Data.Span);

    Assert.Multiple(() => {
      Assert.That(luma, Is.All.InRange(235 * 4 - 4, 235 * 4 + 4));
      Assert.That(cb, Is.All.InRange(128 * 4 - 4, 128 * 4 + 4));
      Assert.That(cr, Is.All.InRange(128 * 4 - 4, 128 * 4 + 4));
    });
  }
}
