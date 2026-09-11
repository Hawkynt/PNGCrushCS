using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

/// <summary>The IV32 writer, checked at the wire, through the decoder beside it and, where present, ffmpeg.</summary>
[TestFixture]
public sealed class Indeo3VideoEncoderTests {

  private const uint _HEADER_ID = 0x46524D48;

  [Test]
  [Category("Unit")]
  public void TheEncoderIsRegisteredUnderIv32() {
    var stream = _Stream(16, 16);

    Assert.That(VideoFormatRegistry.AllEncoders.Select(e => e.CodecName), Does.Contain("Intel Indeo 3"));
    Assert.That(VideoFormatRegistry.CanEncode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateEncoder(stream), Is.InstanceOf<Indeo3VideoEncoder>());
  }

  [Test]
  [Category("Unit")]
  public void TheDescriptionIsAWindowsIv32Stream() {
    var described = Indeo3VideoEncoder.Create(_Stream(32, 24)).DescribeStream();
    var format = described.CodecPrivateData.ToArray();

    Assert.Multiple(() => {
      Assert.That(described.Codec, Is.EqualTo(CodecTag.FromCharacters("IV32")));
      Assert.That(described.Handler, Is.EqualTo(CodecTag.FromCharacters("IV32")));
      Assert.That(described.CodecId, Is.EqualTo("V_MS/VFW/FOURCC"));
      Assert.That(described.Width, Is.EqualTo(32));
      Assert.That(described.Height, Is.EqualTo(24));
      Assert.That(described.BitsPerPixel, Is.EqualTo(24));
      Assert.That(described.TimeBase, Is.EqualTo(new Rational(1, 25)));
      Assert.That(format, Has.Length.EqualTo(40));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format), Is.EqualTo(40), "biSize");
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(4)), Is.EqualTo(32), "biWidth");
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(format.AsSpan(8)), Is.EqualTo(24), "biHeight");
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(format.AsSpan(14)), Is.EqualTo(24), "biBitCount");
      Assert.That(format[16..20], Is.EqualTo("IV32"u8.ToArray()), "biCompression");
    });

    Assert.That(Indeo3VideoDecoder.Accepts(described), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void OneSmallFrameHasTheExpectedHeaderAndThreeWholePlaneCells() {
    var encoder = Indeo3VideoEncoder.Create(_Stream(16, 16));
    Assert.That(encoder.TryEncode(_Flat(16, 16, 128), 7, out var packet), Is.True);
    var frame = packet.Data.ToArray();

    // A 16x16 luma plane is 4 vector-count bytes + 1 tree byte + 1 descriptor + 128 dyad bytes.
    // Each 4x4 chroma plane is the same framing plus eight dyad bytes.
    const int yLength = 134;
    const int chromaLength = 14;
    const int yOffset = 48;
    const int vOffset = yOffset + yLength;
    const int uOffset = vOffset + chromaLength;

    // Sixteen bytes of slack close the frame. The decoder's cell reader may run that far past the
    // last cell, and its header check rejects a frame whose final plane starts within sixteen bytes
    // of the end -- which a 4x4 chroma plane always would.
    const int trailingSlack = 16;
    const int dataSize = uOffset + chromaLength + trailingSlack;

    Assert.Multiple(() => {
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(7));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(7));
      Assert.That(frame, Has.Length.EqualTo(16 + dataSize));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(frame), Is.Zero, "frame number");
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(4)), Is.Zero, "OS word 2");
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(8)),
        Is.EqualTo((uint)dataSize ^ _HEADER_ID), "OS checksum");
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(12)), Is.EqualTo((uint)dataSize));
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(16)), Is.EqualTo(32), "codec version");
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(18)), Is.EqualTo(1 << 2), "key-frame flag");
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(20)), Is.EqualTo((uint)dataSize * 8), "bits");
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(28)), Is.EqualTo(16), "height");
      Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(30)), Is.EqualTo(16), "width");
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(32)), Is.EqualTo(yOffset));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(36)), Is.EqualTo(vOffset));
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(40)), Is.EqualTo(uOffset));
    });

    foreach (var offset in new[] { yOffset, vOffset, uOffset }) {
      var plane = 16 + offset;
      Assert.Multiple(() => {
        Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(plane)), Is.Zero, "motion-vector count");
        Assert.That(frame[plane + 4], Is.EqualTo(0b1011_0000), "MC-tree intra then VQ-tree data");
        Assert.That(frame[plane + 5], Is.Zero, "mode 0, VQ table 0");
      });
    }
  }

  [Test]
  [Category("Unit")]
  public void ConsecutivePicturesStayIndependentKeyFramesAndAdvanceTheFrameNumber() {
    var encoder = Indeo3VideoEncoder.Create(_Stream(16, 16));
    var first = _Flat(16, 16, 32);
    var second = _Flat(16, 16, 220);

    Assert.That(encoder.TryEncode(first, 10, out var one), Is.True);
    Assert.That(encoder.TryEncode(second, 11, out var two), Is.True);

    Assert.Multiple(() => {
      Assert.That(one.IsKeyFrame, Is.True);
      Assert.That(two.IsKeyFrame, Is.True);
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(one.Data.Span), Is.Zero);
      Assert.That(BinaryPrimitives.ReadUInt32LittleEndian(two.Data.Span), Is.EqualTo(1));
    });

    var decoder = Indeo3VideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(one, out _), Is.True);
    Assert.That(decoder.TryDecode(two, out var decoded), Is.True);
    Assert.That(decoded.Width, Is.EqualTo(16));
    Assert.That(decoded.Height, Is.EqualTo(16));
  }

  [Test]
  [Category("Unit")]
  public void AFlatGreyPictureSurvivesTheLossyRoundTripWithinThreeLevels() {
    var encoder = Indeo3VideoEncoder.Create(_Stream(32, 24));
    var source = _Flat(32, 24, 128);
    Assert.That(encoder.TryEncode(source, 0, out var packet), Is.True);

    var decoder = Indeo3VideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));

    for (var i = 0; i < decoded.PixelData.Length; ++i)
      Assert.That(Math.Abs(decoded.PixelData[i] - 128), Is.LessThanOrEqualTo(3), $"sample {i}");
  }

  [Test]
  [Category("Unit")]
  public void AColourGradientProducesACompleteDecodablePicture() {
    const int width = 64;
    const int height = 48;
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)(x * 255 / (width - 1));
      pixels[at + 1] = (byte)(y * 255 / (height - 1));
      pixels[at + 2] = (byte)(((x >> 3) ^ (y >> 3)) % 2 == 0 ? 255 : 0);
    }

    var encoder = Indeo3VideoEncoder.Create(_Stream(width, height));
    Assert.That(encoder.TryEncode(_Rgb(width, height, pixels), 0, out var packet), Is.True);

    var decoder = Indeo3VideoDecoder.Create(encoder.DescribeStream());
    Assert.That(decoder.TryDecode(packet, out var decoded), Is.True);
    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
      Assert.That(decoded.PixelData, Has.Length.EqualTo(width * height * 3));
    });
  }

  [TestCase(12, 16)]
  [TestCase(16, 12)]
  [TestCase(644, 16)]
  [TestCase(16, 484)]
  [TestCase(18, 16)]
  [TestCase(16, 18)]
  [Category("Unit")]
  public void DimensionsOutsideTheDefinedCellGridRefuse(int width, int height)
    => Assert.Throws<NotSupportedException>(() => Indeo3VideoEncoder.Create(_Stream(width, height)));

  [Test]
  [Category("Unit")]
  public void ASizeChangeRefusesInsteadOfSilentlyChangingTheStream() {
    var encoder = Indeo3VideoEncoder.Create(_Stream(16, 16));
    Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Flat(20, 16, 128), 0, out _));
  }

  [Test]
  [Category("Conformance")]
  public void FfmpegReadsTheAviWhenItIsAvailable() {
    FFmpegOracle.RequireAvailable();

    const int width = 64;
    const int height = 48;
    var encoder = Indeo3VideoEncoder.Create(_Stream(width, height));
    Assert.That(encoder.TryEncode(_Flat(width, height, 128), 0, out var packet), Is.True);
    var avi = VideoIO.Mux<AviWriter>([encoder.DescribeStream()], [packet]);
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".avi");

    try {
      File.WriteAllBytes(path, avi);
      var (decoded, output) = FFmpegOracle.TryDecodeFirstFrame(path, width, height);
      Assert.That(decoded, Is.True, output);
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }
  }

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("IV32"),
    Handler = CodecTag.FromCharacters("IV32"),
    Width = width,
    Height = height,
    TimeBase = new Rational(1, 25),
    FrameRate = new Rational(25, 1),
  };

  private static RawImage _Flat(int width, int height, byte level) {
    var pixels = new byte[width * height * 3];
    pixels.AsSpan().Fill(level);
    return _Rgb(width, height, pixels);
  }

  private static RawImage _Rgb(int width, int height, byte[] pixels) => new() {
    Width = width,
    Height = height,
    Format = PixelFormat.Rgb24,
    PixelData = pixels,
  };
}
