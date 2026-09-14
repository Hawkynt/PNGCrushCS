using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.Jpeg;
using FileFormat.Mp4;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public sealed class AvidMeridienCompressedVideoDecoderTests {

  [TestCase("AVDJ", true)]
  [TestCase("avdj", true)]
  [TestCase("MJPG", false)]
  [Category("Unit")]
  public void TakesOnlyAvidMeridienCompressed(string tag, bool expected)
    => Assert.That(AvidMeridienCompressedVideoDecoder.Accepts(_Stream(4, 4, tag: tag)), Is.EqualTo(expected));

  [Test]
  [Category("Unit")]
  public void ProgressivePacketKeepsAvidsBottomRowsWhenContainerHeightIsSmaller() {
    var pixels = new byte[4 * 6 * 3];
    for (var y = 0; y < 6; ++y)
    for (var x = 0; x < 4; ++x) {
      var at = (y * 4 + x) * 3;
      var value = y < 2 ? (byte)230 : (byte)20;
      pixels[at] = pixels[at + 1] = pixels[at + 2] = value;
    }

    var jpeg = _Jpeg(new() { Width = 4, Height = 6, Format = PixelFormat.Rgb24, PixelData = pixels });
    var decoder = AvidMeridienCompressedVideoDecoder.Create(_Stream(4, 4));

    Assert.That(decoder.TryDecode(new(0, jpeg), out var frame), Is.True);
    Assert.Multiple(() => {
      Assert.That(frame.Width, Is.EqualTo(4));
      Assert.That(frame.Height, Is.EqualTo(4));
      Assert.That(frame.PixelData[0], Is.LessThan(80), "the two bright coded rows above the display picture must be cropped");
      Assert.That(frame.PixelData[^1], Is.LessThan(80));
    });
  }

  [Test]
  [Category("Unit")]
  public void WithoutContainerGeometryAProgressivePacketKeepsTheWholeJpeg() {
    var jpeg = _Jpeg(_Solid(4, 6, 77));
    var decoder = AvidMeridienCompressedVideoDecoder.Create(_Stream(0, 0));

    Assert.That(decoder.TryDecode(new(0, jpeg), out var frame), Is.True);
    Assert.That((frame.Width, frame.Height), Is.EqualTo((4, 6)));
  }

  [TestCase(1, false, TestName = "NTSC codec data puts the first/lower field on odd rows")]
  [TestCase(2, true, TestName = "PAL codec data puts the first/upper field on even rows")]
  [Category("Unit")]
  public void AvidCodecDataControlsTwoFieldPolarity(byte standard, bool firstExpectedOnTopRow) {
    var first = _Jpeg(_Solid(4, 2, 35));
    var second = _Jpeg(_Solid(4, 2, 215));
    var packet = first.Concat(second).ToArray();
    var decoder = AvidMeridienCompressedVideoDecoder.Create(_Stream(4, 4, privateData: _AvidExtra(standard)));

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    var top = frame.PixelData[0];
    var next = frame.PixelData[4 * 3];
    Assert.That(top < next, Is.EqualTo(firstExpectedOnTopRow));
  }

  [TestCase(1, true, TestName = "QuickTime TT codes the top field first")]
  [TestCase(6, false, TestName = "QuickTime BB codes the bottom field first")]
  [TestCase(9, true, TestName = "QuickTime TB codes the top field first")]
  [TestCase(14, false, TestName = "QuickTime BT codes the bottom field first")]
  [Category("Unit")]
  public void QuickTimeFielAtomControlsCodedFieldPlacement(byte detail, bool firstExpectedOnTopRow) {
    var first = _Jpeg(_Solid(4, 2, 30));
    var second = _Jpeg(_Solid(4, 2, 220));
    var decoder = AvidMeridienCompressedVideoDecoder.Create(_Stream(4, 4, privateData: _QuickTimeDescription(detail)));

    Assert.That(decoder.TryDecode(new(0, first.Concat(second).ToArray()), out var frame), Is.True);
    var top = frame.PixelData[0];
    var next = frame.PixelData[4 * 3];
    Assert.That(top < next, Is.EqualTo(firstExpectedOnTopRow));
  }

  [Test]
  [Category("Unit")]
  public void InterlacedPacketWithoutItsSecondJpegIsRejected() {
    var decoder = AvidMeridienCompressedVideoDecoder.Create(_Stream(4, 4, privateData: _AvidExtra(1)));
    var first = _Jpeg(_Solid(4, 2, 30));

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, first), out _));
    Assert.That(failure!.Message, Does.Contain("second JPEG field is missing"));
  }

  [Test]
  [Category("Unit")]
  public void InterlacedPacketWhoseFieldsDisagreeIsRejected() {
    var decoder = AvidMeridienCompressedVideoDecoder.Create(_Stream(4, 4, privateData: _AvidExtra(1)));
    var first = _Jpeg(_Solid(4, 2, 30));
    var second = _Jpeg(_Solid(5, 2, 220));

    var failure = Assert.Throws<InvalidDataException>(
      () => decoder.TryDecode(new(0, first.Concat(second).ToArray()), out _));
    Assert.That(failure!.Message, Does.Contain("incompatible decoded layouts"));
  }

  [Test]
  [Category("Unit")]
  public void UnknownNonStandardTwoFieldPolarityIsRefusedInsteadOfGuessed() {
    var decoder = AvidMeridienCompressedVideoDecoder.Create(_Stream(4, 4));
    var first = _Jpeg(_Solid(4, 2, 30));
    var second = _Jpeg(_Solid(4, 2, 220));

    var failure = Assert.Throws<NotSupportedException>(
      () => decoder.TryDecode(new(0, first.Concat(second).ToArray()), out _));
    Assert.That(failure!.Message, Does.Contain("spatial placement of the first coded field"));
  }

  private static MediaStreamInfo _Stream(
    int width,
    int height,
    string tag = "AVDJ",
    ReadOnlyMemory<byte> privateData = default) => new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters(tag),
      Width = width,
      Height = height,
      CodecPrivateData = privateData,
    };

  private static byte[] _AvidExtra(byte standard) {
    var data = new byte[13];
    BinaryPrimitives.WriteUInt32LittleEndian(data, 0x2C);
    BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 0x18);
    data[12] = standard;
    return data;
  }

  private static byte[] _QuickTimeDescription(byte detail) {
    var data = new byte[96]; // 8-byte sample-entry header + 78-byte visual body + 10-byte fiel atom.
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(86), 10);
    "fiel"u8.CopyTo(data.AsSpan(90));
    data[94] = 2;
    data[95] = detail;
    return data;
  }

  private static RawImage _Solid(int width, int height, byte value)
    => new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = Enumerable.Repeat(value, width * height * 3).ToArray(),
    };

  private static byte[] _Jpeg(RawImage image) {
    var encoder = MotionJpegVideoEncoder.Create(new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Width = image.Width,
      Height = image.Height,
    });
    Assert.That(encoder.TryEncode(image, 0, out var packet), Is.True);
    return packet.Data.ToArray();
  }
}

[TestFixture]
public sealed class AvidMeridienCompressedVideoEncoderTests {

  [Test]
  [Category("Unit")]
  public void ProgressiveEncodeIsOneBaselineFourTwoTwoJpegAndAKeyFrame() {
    var encoder = AvidMeridienCompressedVideoEncoder.Create(_Stream(16, 12));
    var source = _Striped(16, 12);

    Assert.That(encoder.TryEncode(source, 17, out var packet), Is.True);
    var data = packet.Data.Span;
    Assert.Multiple(() => {
      Assert.That(JpegChunkLayout.FirstImageLength(data), Is.EqualTo(data.Length));
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(17));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(17));
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(_LumaSampling(data), Is.EqualTo(0x21), "Meridien colour JPEG is 4:2:2");
    });

    var decoded = JpegReader.FromSpan(data);
    Assert.That((decoded.Width, decoded.Height), Is.EqualTo((16, 12)));
  }

  [TestCase(486, true, 6)]
  [TestCase(576, false, 1)]
  [Category("Unit")]
  public void StandardDefinitionEncodeWritesTwoFieldsInDocumentedTemporalOrder(
    int height,
    bool firstFieldOnOddRows,
    byte fielDetail) {
    const int WIDTH = 720;
    var encoder = AvidMeridienCompressedVideoEncoder.Create(_Stream(WIDTH, height));
    var source = _Striped(WIDTH, height);

    Assert.That(encoder.TryEncode(source, 0, out var packet), Is.True);
    var firstLength = JpegChunkLayout.FirstImageLength(packet.Data.Span);
    var second = packet.Data.Span[firstLength..];
    var secondLength = JpegChunkLayout.FirstImageLength(second);
    Assert.Multiple(() => {
      Assert.That(firstLength, Is.GreaterThan(0));
      Assert.That(secondLength, Is.EqualTo(second.Length));
      Assert.That(packet.IsKeyFrame, Is.True);
    });

    var first = JpegReader.FromSpan(packet.Data.Span[..firstLength]);
    var next = JpegReader.FromSpan(second);
    Assert.Multiple(() => {
      Assert.That((first.Width, first.Height), Is.EqualTo((WIDTH, height / 2)));
      Assert.That((next.Width, next.Height), Is.EqualTo((WIDTH, height / 2)));
      Assert.That(first.RgbPixelData![0] > first.RgbPixelData[2], Is.EqualTo(firstFieldOnOddRows),
        "odd source rows are red, even source rows are blue");
      Assert.That(next.RgbPixelData![0] > next.RgbPixelData[2], Is.EqualTo(!firstFieldOnOddRows));
    });

    var description = encoder.DescribeStream();
    Assert.Multiple(() => {
      Assert.That(description.Codec, Is.EqualTo(CodecTag.FromCharacters("AVDJ")));
      Assert.That(description.CodecPrivateData.Span[^2], Is.EqualTo(2));
      Assert.That(description.CodecPrivateData.Span[^1], Is.EqualTo(fielDetail));
    });
  }

  [TestCase(486, 6)]
  [TestCase(576, 1)]
  [Category("Conformance")]
  public void FFmpegDecodesStandardDefinitionQuickTime(int height, byte fielDetail) {
    FFmpegOracle.RequireAvailable();
    const int WIDTH = 720;
    var encoder = AvidMeridienCompressedVideoEncoder.Create(_Stream(WIDTH, height));
    Assert.That(encoder.TryEncode(_Striped(WIDTH, height), 0, out var packet), Is.True);

    var description = encoder.DescribeStream();
    var file = VideoIO.Mux<Mp4Writer>([description], [packet]);
    var parsed = Mp4Container.Streams(Mp4Container.FromBytes(file)).Single();
    Assert.Multiple(() => {
      Assert.That(parsed.Codec, Is.EqualTo(CodecTag.FromCharacters("AVDJ")));
      Assert.That(parsed.Width, Is.EqualTo(WIDTH));
      Assert.That(parsed.Height, Is.EqualTo(height));
      Assert.That(parsed.CodecPrivateData.Span[^2], Is.EqualTo(2));
      Assert.That(parsed.CodecPrivateData.Span[^1], Is.EqualTo(fielDetail));
    });

    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".mov");
    try {
      File.WriteAllBytes(path, file);
      var (decoded, detail) = FFmpegOracle.TryDecodeFirstFrame(path, WIDTH, height);
      Assert.That(decoded, Is.True, detail);
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }
  }

  [Test]
  [Category("Unit")]
  public void ProgressiveDescriptionStatesOneField() {
    var description = AvidMeridienCompressedVideoEncoder.Create(_Stream(64, 48)).DescribeStream();
    Assert.Multiple(() => {
      Assert.That(description.CodecPrivateData.Span[^2], Is.EqualTo(1));
      Assert.That(description.CodecPrivateData.Span[^1], Is.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void OpaqueRgbaIsAcceptedButTransparencyIsRefused() {
    var encoder = AvidMeridienCompressedVideoEncoder.Create(_Stream(4, 4));
    var opaque = new byte[4 * 4 * 4];
    for (var i = 0; i < opaque.Length; i += 4) {
      opaque[i] = 40;
      opaque[i + 1] = 80;
      opaque[i + 2] = 120;
      opaque[i + 3] = 255;
    }

    Assert.That(
      encoder.TryEncode(new() { Width = 4, Height = 4, Format = PixelFormat.Rgba32, PixelData = opaque }, 0, out _),
      Is.True);

    var transparent = (byte[])opaque.Clone();
    transparent[3] = 254;
    var failure = Assert.Throws<NotSupportedException>(
      () => encoder.TryEncode(new() { Width = 4, Height = 4, Format = PixelFormat.Rgba32, PixelData = transparent }, 0, out _));
    Assert.That(failure!.Message, Does.Contain("alpha has no public bitstream description"));
  }

  [Test]
  [Category("Unit")]
  public void WrongGeometryIsRejected() {
    var encoder = AvidMeridienCompressedVideoEncoder.Create(_Stream(16, 12));
    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Striped(8, 12), 0, out _));
    Assert.That(failure!.Message, Does.Contain("created for 16x12"));
  }

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("AVDJ"),
    Handler = CodecTag.FromCharacters("AVDJ"),
    Width = width,
    Height = height,
    TimeBase = new(1, 25),
    FrameRate = new(25, 1),
  };

  /// <summary>Even rows blue, odd rows red; field order is visible after JPEG round-trip.</summary>
  private static RawImage _Striped(int width, int height) {
    var pixels = new byte[checked(width * height * 3)];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      if ((y & 1) == 0) {
        pixels[at] = 20;
        pixels[at + 1] = 20;
        pixels[at + 2] = 220;
      } else {
        pixels[at] = 220;
        pixels[at + 1] = 20;
        pixels[at + 2] = 20;
      }
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static byte _LumaSampling(ReadOnlySpan<byte> jpeg) {
    for (var position = 2; position + 4 <= jpeg.Length;) {
      if (jpeg[position] != 0xFF)
        throw new InvalidDataException($"JPEG marker expected at byte {position}.");

      while (position + 1 < jpeg.Length && jpeg[position + 1] == 0xFF)
        ++position;
      var marker = jpeg[position + 1];
      if (marker == 0xC0) {
        var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg[(position + 2)..]);
        if (length < 11 || position + 2 + length > jpeg.Length)
          throw new InvalidDataException("Invalid SOF0 segment in encoded Meridien JPEG.");
        return jpeg[position + 11]; // first component's H/V sampling-factor byte
      }

      if (marker is 0xD8 or 0xD9 or >= 0xD0 and <= 0xD7) {
        position += 2;
        continue;
      }

      var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(jpeg[(position + 2)..]);
      if (segmentLength < 2 || position + 2 + segmentLength > jpeg.Length)
        throw new InvalidDataException($"Invalid JPEG marker FF {marker:X2}.");
      position += 2 + segmentLength;
    }

    throw new InvalidDataException("Encoded Meridien JPEG has no SOF0 segment.");
  }
}
