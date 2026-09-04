using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Uncompressed012v.Tests;

/// <summary>
/// The 012v encoder, against the package's own decoder and the group written out by hand.
/// </summary>
/// <remarks>
/// The reference has no 012v encoder to compare bytes against, so the encoder as a whole was
/// measured against its decoder: packets written here muxed into an AVI and read back through
/// ffmpeg 9 as <c>yuv422p10le</c> at 12x8, 316x4, 7x5 and 6x3, every sample of every plane identical
/// to the one that went in. What these tests add is the one thing that separates this format from
/// v210 — that a row is exactly its whole groups and is padded nowhere — and what a final group past
/// the picture's width is filled with.
/// </remarks>
[TestFixture]
public class Uncompressed012vVideoEncoderTests {

  private static readonly CodecTag _012V = CodecTag.FromCharacters("012v");

  private static MediaStreamInfo _Stream(int width, int height, int index = 0) => new() {
    Index = index,
    Kind = MediaStreamKind.Video,
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static MediaStreamInfo _Audio(int width, int height) => new() {
    Index = 0, Kind = MediaStreamKind.Audio, Width = width, Height = height,
  };

  private static RawImage _RandomYuv422P10(int width, int height, int seed) {
    var random = new Random(seed);
    var chromaWidth = (width + 1) / 2;
    var pixels = new byte[(width * height + chromaWidth * height * 2) * 2];
    for (var at = 0; at < pixels.Length; at += 2)
      BinaryPrimitives.WriteUInt16LittleEndian(pixels.AsSpan(at), (ushort)random.Next(1024));

    return new() { Width = width, Height = height, Format = PixelFormat.Yuv422P10, PixelData = pixels };
  }

  [Test]
  [Category("Unit")]
  public void DescribesAStreamTheDecoderAccepts() {
    var encoder = Uncompressed012vVideoEncoder.Create(_Stream(316, 4, 2));

    var described = encoder.DescribeStream();

    Assert.Multiple(() => {
      Assert.That(Uncompressed012vVideoEncoder.Codec, Is.EqualTo(_012V));
      Assert.That(described.Codec, Is.EqualTo(_012V));
      Assert.That(described.Kind, Is.EqualTo(MediaStreamKind.Video));
      Assert.That(described.Index, Is.EqualTo(2));
      Assert.That(described.Width, Is.EqualTo(316));
      Assert.That(described.Height, Is.EqualTo(4));
      Assert.That(described.BitsPerPixel, Is.EqualTo(20));
      Assert.That(Uncompressed012vVideoDecoder.Accepts(described), Is.True);
      Assert.That(() => VideoFormatRegistry.CreateDecoder(described), Throws.Nothing);
    });
  }

  [Test]
  [Category("Unit")]
  public void ARowIsItsWholeGroupsAndIsPaddedNowhere() {
    // 316 pixels is 53 whole groups of six, 848 bytes — where v210 would round that up to 896.
    foreach (var (width, height, stride) in new[] { (316, 4, 848), (12, 8, 32), (7, 5, 32), (6, 3, 16) }) {
      var encoder = Uncompressed012vVideoEncoder.Create(_Stream(width, height));
      Assert.That(encoder.TryEncode(_RandomYuv422P10(width, height, width), 0, out var packet), Is.True);

      Assert.That(packet.Data.Length, Is.EqualTo(stride * height), $"{width}x{height}");
    }
  }

  [Test]
  [Category("Unit")]
  public void PacksTheGroupAsV210Does() {
    // One group, six luma and three chroma pairs, all stated.
    var pixels = new byte[(6 + 3 + 3) * 2];
    ushort[] luma = [1, 2, 3, 4, 5, 6];
    ushort[] cb = [10, 11, 12];
    ushort[] cr = [20, 21, 22];
    var at = 0;
    foreach (var plane in new[] { luma, cb, cr })
      foreach (var sample in plane) {
        BinaryPrimitives.WriteUInt16LittleEndian(pixels.AsSpan(at), sample);
        at += 2;
      }

    var frame = new RawImage { Width = 6, Height = 1, Format = PixelFormat.Yuv422P10, PixelData = pixels };
    var encoder = Uncompressed012vVideoEncoder.Create(_Stream(6, 1));

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);

    var data = packet.Data.ToArray();
    Assert.Multiple(() => {
      Assert.That(data, Has.Length.EqualTo(16));
      Assert.That(BitConverter.ToUInt32(data, 0), Is.EqualTo(10u | (1u << 10) | (20u << 20)));
      Assert.That(BitConverter.ToUInt32(data, 4), Is.EqualTo(2u | (11u << 10) | (3u << 20)));
      Assert.That(BitConverter.ToUInt32(data, 8), Is.EqualTo(21u | (4u << 10) | (12u << 20)));
      Assert.That(BitConverter.ToUInt32(data, 12), Is.EqualTo(5u | (22u << 10) | (6u << 20)));
    });
  }

  [Test]
  [Category("Unit")]
  public void AFinalGroupPastTheWidthIsWrittenWholeWithZeros() {
    // Seven pixels is two groups; the five samples past the width are zero, so the decoder that
    // reads and discards them reads nothing that was ever a picture.
    var encoder = Uncompressed012vVideoEncoder.Create(_Stream(7, 1));
    Assert.That(encoder.TryEncode(_RandomYuv422P10(7, 1, 5), 0, out var packet), Is.True);

    var data = packet.Data.ToArray();
    Assert.Multiple(() => {
      Assert.That(data, Has.Length.EqualTo(32));
      // Words 1 to 3 of the second group hold only samples past the picture's own width.
      Assert.That(BitConverter.ToUInt32(data, 20), Is.Zero);
      Assert.That(BitConverter.ToUInt32(data, 24), Is.Zero);
      Assert.That(BitConverter.ToUInt32(data, 28), Is.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void RoundTripsThroughTheDecoderSampleForSample() {
    foreach (var (width, height) in new[] { (12, 8), (316, 4), (7, 5), (6, 3) }) {
      var frame = _RandomYuv422P10(width, height, width * 17 + height);
      var encoder = Uncompressed012vVideoEncoder.Create(_Stream(width, height));
      Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);

      var decoder = Uncompressed012vVideoDecoder.Create(encoder.DescribeStream());
      var (luma, cb, cr) = decoder.DecodePlanes(packet.Data.Span);

      var chromaWidth = (width + 1) / 2;
      var expectedLuma = new ushort[width * height];
      var expectedCb = new ushort[chromaWidth * height];
      var expectedCr = new ushort[chromaWidth * height];
      var source = frame.PixelData.AsSpan();
      for (var i = 0; i < expectedLuma.Length; ++i)
        expectedLuma[i] = BinaryPrimitives.ReadUInt16LittleEndian(source[(i * 2)..]);
      var chromaBase = expectedLuma.Length * 2;
      for (var i = 0; i < expectedCb.Length; ++i) {
        expectedCb[i] = BinaryPrimitives.ReadUInt16LittleEndian(source[(chromaBase + i * 2)..]);
        expectedCr[i] = BinaryPrimitives.ReadUInt16LittleEndian(source[(chromaBase + expectedCb.Length * 2 + i * 2)..]);
      }

      Assert.Multiple(() => {
        Assert.That(luma, Is.EqualTo(expectedLuma).AsCollection, $"luma {width}x{height}");
        Assert.That(cb, Is.EqualTo(expectedCb).AsCollection, $"cb {width}x{height}");
        Assert.That(cr, Is.EqualTo(expectedCr).AsCollection, $"cr {width}x{height}");
      });
    }
  }

  [Test]
  [Category("Unit")]
  public void RefusesWhatItCannotWrite() {
    var encoder = Uncompressed012vVideoEncoder.Create(_Stream(12, 4));

    Assert.Multiple(() => {
      Assert.That(() => Uncompressed012vVideoEncoder.Create(_Audio(12, 4)), Throws.TypeOf<NotSupportedException>());
      Assert.That(() => Uncompressed012vVideoEncoder.Create(_Stream(12, 0)), Throws.TypeOf<InvalidDataException>());
      Assert.That(() => encoder.TryEncode(_RandomYuv422P10(13, 4, 1), 0, out _), Throws.TypeOf<InvalidDataException>());
      Assert.That(() => encoder.TryEncode(null!, 0, out _), Throws.TypeOf<ArgumentNullException>());
    });
  }

  [Test]
  [Category("Unit")]
  public void RefusesASampleWiderThanTenBits() {
    var frame = _RandomYuv422P10(6, 1, 2);
    BinaryPrimitives.WriteUInt16LittleEndian(frame.PixelData.AsSpan(), 1024);
    var encoder = Uncompressed012vVideoEncoder.Create(_Stream(6, 1));

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(frame, 0, out _));
    Assert.That(failure!.Message, Does.Contain("ten bits wide"));
  }
}
