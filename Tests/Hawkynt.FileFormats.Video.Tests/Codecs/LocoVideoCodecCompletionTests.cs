extern alias Images;
using System;
using System.Buffers.Binary;
using System.IO;
using BitmapInfoHeader = Images::FileFormat.Bmp.BitmapInfoHeader;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class LocoVideoCodecCompletionTests {

  [Test]
  [Category("Unit")]
  public void Yuv422DecoderPreservesTheCodedSamples() {
    var decoder = LocoVideoDecoder.Create(_Stream(2, 1, mode: 1));

    Assert.That(decoder.TryDecode(new(0, new byte[] { 0x8A, 0xA0, 0xC0 }), out var frame), Is.True);
    Assert.Multiple(() => {
      Assert.That(frame.Format, Is.EqualTo(PixelFormat.Yuv422P8));
      Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 128, 128, 129, 130 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void Yuv420DecoderPreservesTheCodedSamplesAndYv12PlaneOrder() {
    var decoder = LocoVideoDecoder.Create(_Stream(2, 2, mode: 5));

    Assert.That(decoder.TryDecode(new(0, new byte[] { 0x8E, 0xC0, 0xA0 }), out var frame), Is.True);
    Assert.Multiple(() => {
      Assert.That(frame.Format, Is.EqualTo(PixelFormat.Yuv420P8));
      Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 128, 128, 128, 128, 129, 130 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void VersionTwoNearLosslessStepIsAppliedWhileReading() {
    var decoder = LocoVideoDecoder.Create(_Stream(1, 1, mode: 3, version: 2, lossy: 1));

    // Unsigned Rice value two maps to +2 when the version-two near-lossless step is one.
    Assert.That(decoder.TryDecode(new(0, new byte[] { 0xA0, 0xA0, 0xA0 }), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 130, 130, 130 }));
  }

  [Test]
  [Category("Unit")]
  public void Yuv422EncoderWritesTheExpectedThreePlanesAndRoundTrips() {
    var encoder = LocoVideoEncoder.Create(_Stream(2, 1, mode: 1));
    var pixels = new byte[] { 128, 128, 129, 130 };

    Assert.That(encoder.TryEncode(new() {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Yuv422P8,
      PixelData = pixels,
    }, 7, out var packet), Is.True);

    Assert.Multiple(() => {
      Assert.That(packet.Data.ToArray(), Is.EqualTo(new byte[] { 0x8A, 0xA0, 0xC0 }));
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(encoder.DescribeStream().BitsPerPixel, Is.EqualTo(16));
    });

    var decoder = LocoVideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.Multiple(() => {
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Yuv422P8));
      Assert.That(decoded.PixelData, Is.EqualTo(pixels));
    });
  }

  [Test]
  [Category("Unit")]
  public void Yuv420EncoderWritesYThenVThenUAndRoundTripsToCanonicalYuv() {
    var encoder = LocoVideoEncoder.Create(_Stream(2, 2, mode: 5));
    var pixels = new byte[] { 128, 128, 128, 128, 129, 130 };

    Assert.That(encoder.TryEncode(new() {
      Width = 2,
      Height = 2,
      Format = PixelFormat.Yuv420P8,
      PixelData = pixels,
    }, null, out var packet), Is.True);

    Assert.Multiple(() => {
      Assert.That(packet.Data.ToArray(), Is.EqualTo(new byte[] { 0x8E, 0xC0, 0xA0 }));
      Assert.That(encoder.DescribeStream().BitsPerPixel, Is.EqualTo(12));
      Assert.That(_Mode(encoder.DescribeStream()), Is.EqualTo(5));
    });

    var decoder = LocoVideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.That(decoded.PixelData, Is.EqualTo(pixels));
  }

  [TestCase(12, PixelFormat.Yuv420P8)]
  [TestCase(16, PixelFormat.Yuv422P8)]
  [Category("Conformance")]
  public void NativeYuvEncoderIsAcceptedByFfmpeg(int bitsPerPixel, PixelFormat format) {
    FFmpegOracle.RequireAvailable();

    const int width = 8;
    const int height = 4;
    var requested = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("LOCO"),
      Handler = CodecTag.FromCharacters("LOCO"),
      Width = width,
      Height = height,
      BitsPerPixel = bitsPerPixel,
      TimeBase = new Rational(1, 25),
      FrameRate = new Rational(25, 1),
    };
    var encoder = LocoVideoEncoder.Create(requested);
    var frame = _NativeYuv(width, height, format);

    Assert.That(encoder.TryEncode(frame, 0, out var packet), Is.True);
    var avi = VideoIO.Mux<AviWriter>([encoder.DescribeStream()], [packet]);
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");
    try {
      File.WriteAllBytes(path, avi);
      var (decoded, detail) = FFmpegOracle.TryDecodeFirstFrame(path, width, height);
      Assert.That(decoded, Is.True, detail);
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }
  }

  [Test]
  [Category("Unit")]
  public void BitsPerPixelSelectsNativeYuvWhenNoTrailerWasSupplied() {
    var encoder = LocoVideoEncoder.Create(new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("LOCO"),
      Width = 2,
      Height = 1,
      BitsPerPixel = 16,
    });

    Assert.Multiple(() => {
      Assert.That(_Mode(encoder.DescribeStream()), Is.EqualTo(1));
      Assert.That(encoder.DescribeStream().BitsPerPixel, Is.EqualTo(16));
    });
  }

  [TestCase(-1, 16)]
  [TestCase(1, 16)]
  [TestCase(2, 16)]
  [TestCase(-2, 24)]
  [TestCase(3, 24)]
  [TestCase(-3, 32)]
  [TestCase(4, 32)]
  [TestCase(-4, 12)]
  [TestCase(5, 12)]
  [Category("Unit")]
  public void EncoderPreservesEveryDeclaredColourMode(int mode, int bitsPerPixel) {
    var encoder = LocoVideoEncoder.Create(_Stream(2, 2, mode));

    Assert.Multiple(() => {
      Assert.That(_Mode(encoder.DescribeStream()), Is.EqualTo(mode));
      Assert.That(encoder.DescribeStream().BitsPerPixel, Is.EqualTo(bitsPerPixel));
    });
  }

  [Test]
  [Category("Unit")]
  public void YuvEncoderRefusesRgbInsteadOfSilentlyChangingSamples() {
    var encoder = LocoVideoEncoder.Create(_Stream(2, 1, mode: 1));
    var frame = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[6],
    };

    var exception = Assert.Throws<NotSupportedException>(() => encoder.TryEncode(frame, null, out _));
    Assert.That(exception!.Message, Does.Contain(nameof(PixelFormat.Yuv422P8)));
  }

  private static RawImage _NativeYuv(int width, int height, PixelFormat format) {
    var chromaWidth = width >> 1;
    var chromaHeight = format == PixelFormat.Yuv420P8 ? height >> 1 : height;
    var yLength = checked(width * height);
    var chromaLength = checked(chromaWidth * chromaHeight);
    var data = new byte[checked(yLength + 2 * chromaLength)];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        data[y * width + x] = (byte)(32 + x * 17 + y * 7);

    var uAt = yLength;
    var vAt = yLength + chromaLength;
    for (var y = 0; y < chromaHeight; ++y)
      for (var x = 0; x < chromaWidth; ++x) {
        data[uAt + y * chromaWidth + x] = (byte)(80 + x * 9 + y * 3);
        data[vAt + y * chromaWidth + x] = (byte)(160 - x * 7 - y * 5);
      }

    return new() { Width = width, Height = height, Format = format, PixelData = data };
  }

  private static MediaStreamInfo _Stream(int width, int height, int mode, int version = 1, int lossy = 0) {
    var format = new byte[BitmapInfoHeader.StructSize + 12];
    var extra = format.AsSpan(BitmapInfoHeader.StructSize);
    BinaryPrimitives.WriteInt32LittleEndian(extra, version);
    BinaryPrimitives.WriteInt32LittleEndian(extra[4..], mode);
    BinaryPrimitives.WriteInt32LittleEndian(extra[8..], lossy);

    return new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("LOCO"),
      Width = width,
      Height = height,
      BitsPerPixel = mode switch {
        -1 or 1 or 2 => 16,
        -2 or 3 => 24,
        -3 or 4 => 32,
        -4 or 5 => 12,
        _ => 0,
      },
      CodecPrivateData = format,
    };
  }

  private static int _Mode(MediaStreamInfo stream)
    => BinaryPrimitives.ReadInt32LittleEndian(stream.CodecPrivateData.Span[(BitmapInfoHeader.StructSize + 4)..]);
}
