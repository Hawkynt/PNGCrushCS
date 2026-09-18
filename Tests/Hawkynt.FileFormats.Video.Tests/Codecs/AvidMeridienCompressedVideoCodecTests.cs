extern alias Images;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.Mp4;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;
using JpegChunkLayout = Images::FileFormat.Jpeg.JpegChunkLayout;
using JpegReader = Images::FileFormat.Jpeg.JpegReader;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The Avid Meridien Compressed (<c>AVDJ</c>) reader: the display crop, the two-field weave, and
/// where the weave's row parity comes from.
/// </summary>
/// <remarks>
/// Neither of the two things this decoder does beyond handing bytes to a JPEG reader is visible in
/// the JPEG, so both are checked with fixtures whose own colours say which way round the answer came
/// out: a picture whose top and bottom halves differ says which end the crop kept, and two flat
/// fields of different brightness say which output rows each landed on.
/// </remarks>
[TestFixture]
public sealed class AvidMeridienCompressedVideoDecoderTests {

  // ============================================================================================
  // Identity
  // ============================================================================================

  [TestCase("AVDJ", true)]
  [TestCase("avdj", true)]
  [TestCase("MJPG", false)]
  [TestCase("AVRn", false)]
  [Category("Unit")]
  public void TakesOnlyAvidMeridienCompressed(string tag, bool expected)
    => Assert.That(AvidMeridienCompressedVideoDecoder.Accepts(_Stream(4, 4, tag: tag)), Is.EqualTo(expected));

  [Test]
  [Category("Unit")]
  public void AnAudioStreamTaggedAvdjIsStillNotVideo() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Audio,
      Codec = CodecTag.FromCharacters("AVDJ"),
    };

    Assert.That(AvidMeridienCompressedVideoDecoder.Accepts(stream), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void NeitherEntryPointTakesNull() {
    Assert.Multiple(() => {
      Assert.Throws<ArgumentNullException>(() => AvidMeridienCompressedVideoDecoder.Accepts(null!));
      Assert.Throws<ArgumentNullException>(() => AvidMeridienCompressedVideoDecoder.Create(null!));
    });
  }

  // ============================================================================================
  // The progressive picture and its display crop
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ProgressivePacketKeepsAvidsBottomRowsWhenContainerHeightIsSmaller() {
    var jpeg = _Jpeg(_SplitHalves(4, 6, top: 230, bottom: 20));
    var decoder = AvidMeridienCompressedVideoDecoder.Create(_Stream(4, 4));

    Assert.That(decoder.TryDecode(new(0, jpeg), out var frame), Is.True);
    Assert.Multiple(() => {
      Assert.That((frame.Width, frame.Height), Is.EqualTo((4, 4)));
      Assert.That(frame.PixelData[0], Is.LessThan(80), "the two bright coded rows above the display picture must be cropped");
      Assert.That(frame.PixelData[^1], Is.LessThan(80));
    });
  }

  [Test]
  [Category("Unit")]
  public void ProgressivePacketKeepsTheLeftColumnsWhenContainerWidthIsSmaller() {
    var jpeg = _Jpeg(_Solid(8, 4, 90));
    var decoder = AvidMeridienCompressedVideoDecoder.Create(_Stream(4, 4));

    Assert.That(decoder.TryDecode(new(0, jpeg), out var frame), Is.True);
    Assert.That((frame.Width, frame.Height), Is.EqualTo((4, 4)));
  }

  [Test]
  [Category("Unit")]
  public void WithoutContainerGeometryAProgressivePacketKeepsTheWholeJpeg() {
    var jpeg = _Jpeg(_Solid(4, 6, 77));
    var decoder = AvidMeridienCompressedVideoDecoder.Create(_Stream(0, 0));

    Assert.That(decoder.TryDecode(new(0, jpeg), out var frame), Is.True);
    Assert.That((frame.Width, frame.Height), Is.EqualTo((4, 6)));
  }

  [TestCase(8, 4, TestName = "a container wider than the coded picture")]
  [TestCase(4, 5, TestName = "a container taller than the coded picture")]
  [Category("Unit")]
  public void AContainerLargerThanTheCodedPictureIsRefusedRatherThanPadded(int width, int height) {
    var jpeg = _Jpeg(_Solid(4, 4, 77));
    var decoder = AvidMeridienCompressedVideoDecoder.Create(_Stream(width, height));

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, jpeg), out _));
    Assert.That(failure!.Message, Does.Contain("larger than the 4x4"));
  }

  [TestCase(6, 8, false, TestName = "a coded picture three quarters of the frame height is a whole frame")]
  [TestCase(5, 8, true, TestName = "a coded picture under three quarters of the frame height is one field")]
  [Category("Unit")]
  public void TheFieldTestSitsAtThreeQuartersOfTheStatedFrameHeight(int codedHeight, int frameHeight, bool isField) {
    // One JPEG only. A packet read as two fields then complains about the missing second one, which
    // is what says which side of the boundary the reader landed on.
    var decoder = AvidMeridienCompressedVideoDecoder.Create(_Stream(4, frameHeight, privateData: _AvidExtra(1)));
    var packet = new CodedPacket(0, _Jpeg(_Solid(4, codedHeight, 90)));

    if (isField) {
      var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(packet, out _));
      Assert.That(failure!.Message, Does.Contain("second JPEG field is missing"));
      return;
    }

    var refusal = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(packet, out _));
    Assert.That(refusal!.Message, Does.Contain("larger than the"), "a whole frame shorter than the container is refused, not woven");
  }

  // ============================================================================================
  // Where the two-field row parity comes from
  // ============================================================================================

  [TestCase(1, false, TestName = "NTSC codec data puts the first coded field on odd rows")]
  [TestCase(2, true, TestName = "PAL codec data puts the first coded field on even rows")]
  [Category("Unit")]
  public void AvidVideoForWindowsCodecDataControlsTwoFieldPlacement(byte standard, bool firstExpectedOnTopRow)
    => _AssertFirstFieldOnTopRow(_AvidExtra(standard), firstExpectedOnTopRow);

  [TestCase(1, false)]
  [TestCase(2, true)]
  [Category("Unit")]
  public void TheSameCodecDataIsFoundBehindACompleteBitmapInfoHeader(byte standard, bool firstExpectedOnTopRow)
    => _AssertFirstFieldOnTopRow(_BehindBitmapInfoHeader(_AvidExtra(standard)), firstExpectedOnTopRow);

  [TestCase((byte)1, true, TestName = "QuickTime TT stores the top field first")]
  [TestCase((byte)9, true, TestName = "QuickTime TB stores the top field first")]
  [TestCase((byte)6, false, TestName = "QuickTime BB stores the bottom field first")]
  [TestCase((byte)14, false, TestName = "QuickTime BT stores the bottom field first")]
  [Category("Unit")]
  public void TheQuickTimeFielExtensionControlsTwoFieldPlacement(byte detail, bool firstExpectedOnTopRow)
    => _AssertFirstFieldOnTopRow(_QuickTimeDescription(fields: 2, detail), firstExpectedOnTopRow);

  [TestCase(486, false, TestName = "a 525-line D1 frame stores its lower field first")]
  [TestCase(576, true, TestName = "a 625-line D1 frame stores its upper field first")]
  [Category("Unit")]
  public void WithNoCodecDataTheTwoD1RastersHaveAKnownFieldOrder(int height, bool firstExpectedOnTopRow) {
    var decoder = AvidMeridienCompressedVideoDecoder.Create(_Stream(4, height));
    var packet = _Packet(_Jpeg(_Solid(4, height / 2, 30)), _Jpeg(_Solid(4, height / 2, 220)));

    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);
    Assert.That(frame.PixelData[0] < frame.PixelData[4 * 3], Is.EqualTo(firstExpectedOnTopRow));
  }

  [Test]
  [Category("Unit")]
  public void AFielExtensionStatingOneFieldSaysNothingAboutPlacement() {
    // fields=1 is a progressive descriptor; the 486-line raster's own known order has to answer.
    var decoder = AvidMeridienCompressedVideoDecoder.Create(
      _Stream(4, 486, privateData: _QuickTimeDescription(fields: 1, detail: 0)));
    var packet = _Packet(_Jpeg(_Solid(4, 243, 30)), _Jpeg(_Solid(4, 243, 220)));

    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);
    Assert.That(frame.PixelData[0], Is.GreaterThan(frame.PixelData[4 * 3]), "486 lines means the first coded field is the lower one");
  }

  [Test]
  [Category("Unit")]
  public void UnknownNonStandardTwoFieldPolarityIsRefusedInsteadOfGuessed() {
    var decoder = AvidMeridienCompressedVideoDecoder.Create(_Stream(4, 4));
    var packet = _Packet(_Jpeg(_Solid(4, 2, 30)), _Jpeg(_Solid(4, 2, 220)));

    var failure = Assert.Throws<NotSupportedException>(() => decoder.TryDecode(packet, out _));
    Assert.That(failure!.Message, Does.Contain("spatial placement of the first coded field"));
  }

  [Test]
  [Category("Unit")]
  public void AWovenFrameTallerThanTheContainerIsCroppedAtItsTopToo() {
    // Two coded fields of four rows weave into eight, against a container stating six. The two rules
    // meet here and their order matters: cropping a four-row field to six rows would refuse the
    // packet outright, and weaving without cropping would hand back a frame two rows too tall.
    var decoder = AvidMeridienCompressedVideoDecoder.Create(_Stream(4, 6, privateData: _AvidExtra(2)));
    var packet = _Packet(_Jpeg(_Solid(4, 4, 30)), _Jpeg(_Solid(4, 4, 220)));

    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);
    Assert.Multiple(() => {
      Assert.That((frame.Width, frame.Height), Is.EqualTo((4, 6)));
      Assert.That(frame.PixelData[0], Is.LessThan(frame.PixelData[4 * 3]),
        "PAL puts the darker first coded field on even rows, and dropping two rows keeps that parity");
    });
  }

  // ============================================================================================
  // Malformed packets
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void PaddingBetweenTheTwoFieldsIsSkipped() {
    var decoder = AvidMeridienCompressedVideoDecoder.Create(_Stream(4, 4, privateData: _AvidExtra(2)));
    var packet = _Packet(_Jpeg(_Solid(4, 2, 30)), new byte[5], _Jpeg(_Solid(4, 2, 220)));

    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);
    Assert.That(frame.PixelData[0], Is.LessThan(frame.PixelData[4 * 3]));
  }

  [Test]
  [Category("Unit")]
  public void InterlacedPacketWithoutItsSecondJpegIsRejected() {
    var decoder = AvidMeridienCompressedVideoDecoder.Create(_Stream(4, 4, privateData: _AvidExtra(1)));

    var failure = Assert.Throws<InvalidDataException>(
      () => decoder.TryDecode(new(0, _Jpeg(_Solid(4, 2, 30))), out _));
    Assert.That(failure!.Message, Does.Contain("second JPEG field is missing"));
  }

  [Test]
  [Category("Unit")]
  public void InterlacedPacketWhoseSecondJpegIsTruncatedIsRejected() {
    var decoder = AvidMeridienCompressedVideoDecoder.Create(_Stream(4, 4, privateData: _AvidExtra(1)));
    var second = _Jpeg(_Solid(4, 2, 220));
    var packet = _Packet(_Jpeg(_Solid(4, 2, 30)), second[..(second.Length / 2)]);

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(packet, out _));
    Assert.That(failure!.Message, Does.Contain("not a complete JPEG picture"));
  }

  [Test]
  [Category("Unit")]
  public void InterlacedPacketWhoseFieldsDisagreeIsRejected() {
    var decoder = AvidMeridienCompressedVideoDecoder.Create(_Stream(4, 4, privateData: _AvidExtra(1)));
    var packet = _Packet(_Jpeg(_Solid(4, 2, 30)), _Jpeg(_Solid(5, 2, 220)));

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(packet, out _));
    Assert.That(failure!.Message, Does.Contain("incompatible decoded layouts"));
  }

  [TestCase(0, TestName = "an empty packet")]
  [TestCase(3, TestName = "three bytes that are not a JPEG")]
  [Category("Unit")]
  public void APacketThatIsNotAJpegIsRejected(int length) {
    var decoder = AvidMeridienCompressedVideoDecoder.Create(_Stream(4, 4));

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, new byte[length]), out _));
    Assert.That(failure!.Message, Does.Contain("first field is not a complete JPEG picture"));
  }

  // ============================================================================================
  // Fixtures
  // ============================================================================================

  private static void _AssertFirstFieldOnTopRow(byte[] privateData, bool firstExpectedOnTopRow) {
    var decoder = AvidMeridienCompressedVideoDecoder.Create(_Stream(4, 4, privateData: privateData));
    var packet = _Packet(_Jpeg(_Solid(4, 2, 30)), _Jpeg(_Solid(4, 2, 220)));

    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);
    Assert.That(frame.PixelData[0] < frame.PixelData[4 * 3], Is.EqualTo(firstExpectedOnTopRow),
      "the first coded field is the darker one");
  }

  internal static MediaStreamInfo _Stream(
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

  private static byte[] _BehindBitmapInfoHeader(byte[] extra) {
    var data = new byte[40 + extra.Length];
    BinaryPrimitives.WriteUInt32LittleEndian(data, 40);
    extra.CopyTo(data.AsSpan(40));
    return data;
  }

  /// <summary>An 8-byte box header, the 78-byte visual body, and a <c>fiel</c> extension.</summary>
  private static byte[] _QuickTimeDescription(byte fields, byte detail) {
    var data = new byte[8 + 78 + 10];
    BinaryPrimitives.WriteUInt32BigEndian(data, (uint)data.Length);
    "AVDJ"u8.CopyTo(data.AsSpan(4));
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8 + 78), 10);
    "fiel"u8.CopyTo(data.AsSpan(8 + 78 + 4));
    data[8 + 78 + 8] = fields;
    data[8 + 78 + 9] = detail;
    return data;
  }

  internal static RawImage _Solid(int width, int height, byte value)
    => new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = Enumerable.Repeat(value, width * height * 3).ToArray(),
    };

  private static RawImage _SplitHalves(int width, int height, byte top, byte bottom) {
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = pixels[at + 1] = pixels[at + 2] = y < height / 3 ? top : bottom;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  internal static byte[] _Jpeg(RawImage image) {
    var encoder = MotionJpegVideoEncoder.Create(new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Width = image.Width,
      Height = image.Height,
    });
    Assert.That(encoder.TryEncode(image, 0, out var packet), Is.True);
    return packet.Data.ToArray();
  }

  private static CodedPacket _Packet(params byte[][] parts) {
    var data = new byte[parts.Sum(p => p.Length)];
    var at = 0;
    foreach (var part in parts) {
      part.CopyTo(data.AsSpan(at));
      at += part.Length;
    }

    return new(0, data);
  }
}

/// <summary>
/// The Avid Meridien Compressed writer: what it codes, what it says about it, and what it refuses.
/// </summary>
/// <remarks>
/// The claim that matters is the conformance one at the bottom — ffmpeg decoding a whole clip of
/// what this writes and every picture coming back the one that went in. The unit tests above it
/// isolate the parts a whole-file comparison cannot attribute: which JPEG holds which field, and the
/// two bytes of the sample description that tell a reader so.
/// </remarks>
[TestFixture]
public sealed class AvidMeridienCompressedVideoEncoderTests {

  private const int _ORACLE_FRAMES = 5;

  // ============================================================================================
  // Construction
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void CreateRefusesWhatItCannotCode() {
    Assert.Multiple(() => {
      Assert.Throws<ArgumentNullException>(() => AvidMeridienCompressedVideoEncoder.Create(null!));
      Assert.Throws<NotSupportedException>(() => AvidMeridienCompressedVideoEncoder.Create(new() {
        Index = 0, Kind = MediaStreamKind.Audio, Width = 16, Height = 16,
      }));
      Assert.Throws<NotSupportedException>(() => AvidMeridienCompressedVideoEncoder.Create(_Stream(0, 16)));
      Assert.Throws<NotSupportedException>(() => AvidMeridienCompressedVideoEncoder.Create(_Stream(16, 0)));
      Assert.Throws<NotSupportedException>(() => AvidMeridienCompressedVideoEncoder.Create(_Stream(70000, 16)));
    });
  }

  [Test]
  [Category("Unit")]
  public void ItAnswersToTheAvdjCodeAndNamesItself() {
    Assert.Multiple(() => {
      Assert.That(AvidMeridienCompressedVideoEncoder.Codec, Is.EqualTo(CodecTag.FromCharacters("AVDJ")));
      Assert.That(AvidMeridienCompressedVideoEncoder.CodecName, Is.EqualTo("Avid Meridien Compressed"));
      Assert.That(VideoFormatRegistry.AllEncoders.Select(e => e.CodecName), Does.Contain("Avid Meridien Compressed"));
      Assert.That(VideoFormatRegistry.AllCodecs.Select(c => c.CodecName), Does.Contain("Avid Meridien Compressed"));
    });
  }

  // ============================================================================================
  // What a packet is
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ProgressiveEncodeIsOneBaselineFourTwoTwoJpegAndAKeyFrame() {
    var encoder = AvidMeridienCompressedVideoEncoder.Create(_Stream(16, 12));

    Assert.That(encoder.TryEncode(_Striped(16, 12, 0), 17, out var packet), Is.True);
    var data = packet.Data.ToArray();
    var firstImageLength = JpegChunkLayout.FirstImageLength(data);
    var sampling = _LumaSampling(data);
    Assert.Multiple(() => {
      Assert.That(firstImageLength, Is.EqualTo(data.Length), "one whole JPEG and nothing after it");
      Assert.That(packet.PresentationTimestamp, Is.EqualTo(17));
      Assert.That(packet.DecodeTimestamp, Is.EqualTo(17));
      Assert.That(packet.IsKeyFrame, Is.True);
      Assert.That(sampling, Is.EqualTo(0x21), "Meridien colour JPEG is 4:2:2");
    });

    var decoded = JpegReader.FromSpan(data);
    Assert.That((decoded.Width, decoded.Height), Is.EqualTo((16, 12)));
  }

  [TestCase(486, true, (byte)6, TestName = "525-line NTSC codes its lower field first and says BB")]
  [TestCase(576, false, (byte)1, TestName = "625-line PAL codes its upper field first and says TT")]
  [Category("Unit")]
  public void StandardDefinitionEncodeWritesTwoFieldsInTheDocumentedOrder(
    int height, bool firstFieldOnOddRows, byte fielDetail) {
    const int WIDTH = 720;
    var encoder = AvidMeridienCompressedVideoEncoder.Create(_Stream(WIDTH, height));

    Assert.That(encoder.TryEncode(_Striped(WIDTH, height, 0), 0, out var packet), Is.True);
    var data = packet.Data.ToArray();
    var firstLength = JpegChunkLayout.FirstImageLength(data);
    var second = data[firstLength..];
    var secondLength = JpegChunkLayout.FirstImageLength(second);
    Assert.Multiple(() => {
      Assert.That(firstLength, Is.GreaterThan(0));
      Assert.That(secondLength, Is.EqualTo(second.Length), "two whole JPEGs and nothing after them");
      Assert.That(packet.IsKeyFrame, Is.True);
    });

    var first = JpegReader.FromSpan(data.AsSpan(0, firstLength));
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
      Assert.That(description.CodecPrivateData.Span[^2], Is.EqualTo(2), "two fields");
      Assert.That(description.CodecPrivateData.Span[^1], Is.EqualTo(fielDetail));
    });
  }

  [Test]
  [Category("Unit")]
  public void ProgressiveDescriptionStatesOneFieldAndClaimsTheMatroskaMotionJpegIdentity() {
    var description = AvidMeridienCompressedVideoEncoder.Create(_Stream(64, 48)).DescribeStream();
    Assert.Multiple(() => {
      Assert.That(description.CodecPrivateData.Span[^2], Is.EqualTo(1));
      Assert.That(description.CodecPrivateData.Span[^1], Is.Zero);
      Assert.That(description.CodecId, Is.EqualTo("V_MJPEG"));
    });
  }

  [Test]
  [Category("Unit")]
  public void AnInterlacedStreamClaimsNoMatroskaIdentityRatherThanOneThatWouldHalveIt() {
    var description = AvidMeridienCompressedVideoEncoder.Create(_Stream(720, 486)).DescribeStream();
    Assert.That(description.CodecId, Is.Null,
      "V_MJPEG over a two-field packet reads back as its first field alone, silently");
  }

  // ============================================================================================
  // What it refuses
  // ============================================================================================

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
  public void APictureOfTheWrongGeometryOrTooFewBytesIsRefused() {
    var encoder = AvidMeridienCompressedVideoEncoder.Create(_Stream(16, 12));
    var short16X12 = new RawImage {
      Width = 16, Height = 12, Format = PixelFormat.Rgb24, PixelData = new byte[16 * 12 * 3 - 1],
    };

    Assert.Multiple(() => {
      Assert.Throws<ArgumentNullException>(() => encoder.TryEncode(null!, 0, out _));
      Assert.That(
        Assert.Throws<InvalidDataException>(() => encoder.TryEncode(_Striped(8, 12, 0), 0, out _))!.Message,
        Does.Contain("created for 16x12"));
      Assert.That(
        Assert.Throws<InvalidDataException>(() => encoder.TryEncode(short16X12, 0, out _))!.Message,
        Does.Contain("needs 576 bytes"));
    });
  }

  // ============================================================================================
  // Round trip through this package's own reader
  // ============================================================================================

  [TestCase(720, 486, TestName = "525-line D1 through a QuickTime file")]
  [TestCase(720, 576, TestName = "625-line D1 through a QuickTime file")]
  [TestCase(320, 240, TestName = "a progressive geometry through a QuickTime file")]
  [Category("Unit")]
  public void EveryFrameComesBackThroughAQuickTimeFileAndTheRegistry(int width, int height) {
    var sources = _Clip(width, height, _ORACLE_FRAMES);
    var file = _Mux(width, height, sources, out var description);

    var parsed = Mp4Container.Streams(Mp4Container.FromBytes(file)).Single();
    Assert.Multiple(() => {
      Assert.That(parsed.Codec, Is.EqualTo(CodecTag.FromCharacters("AVDJ")));
      Assert.That((parsed.Width, parsed.Height), Is.EqualTo((width, height)));
      Assert.That(parsed.CodecPrivateData.Span[^2], Is.EqualTo(description.CodecPrivateData.Span[^2]));
      Assert.That(parsed.CodecPrivateData.Span[^1], Is.EqualTo(description.CodecPrivateData.Span[^1]));
    });

    var decoded = VideoFormatRegistry.DecodeFrames(file).Select(f => f.Image).ToList();
    Assert.That(decoded, Has.Count.EqualTo(sources.Count));
    for (var index = 0; index < sources.Count; ++index) {
      Assert.That((decoded[index].Width, decoded[index].Height), Is.EqualTo((width, height)), $"frame {index}");
      Assert.That(_MeanAbsoluteError(sources[index].PixelData, decoded[index].ToRgb24()), Is.LessThan(_TOLERANCE),
        $"frame {index} did not come back the picture that went in");
    }
  }

  // ============================================================================================
  // The oracle: ffmpeg reads the file and every picture in it
  // ============================================================================================

  [TestCase(720, 486, TestName = "ffmpeg reads every frame of a 525-line interlaced clip")]
  [TestCase(720, 576, TestName = "ffmpeg reads every frame of a 625-line interlaced clip")]
  [TestCase(320, 240, TestName = "ffmpeg reads every frame of a progressive clip")]
  [Category("Conformance")]
  public void FFmpegDecodesEveryFrameOfWhatThisWrites(int width, int height) {
    FFmpegOracle.RequireAvailable();

    var sources = _Clip(width, height, _ORACLE_FRAMES);
    var file = _Mux(width, height, sources, out _);
    var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".mov");

    try {
      File.WriteAllBytes(path, file);
      var (decoded, detail, pictures) = FFmpegOracle.TryDecodePictures(path, width, height, sources.Count);
      Assert.That(decoded, Is.True, detail);

      // Every frame, not the first and not an average over the clip. A field weave the wrong way
      // round, a crop at the wrong end and a stuck reference all produce the right number of
      // right-sized pictures; only comparing each one against its own source sees them.
      var frameBytes = width * height * 3;
      for (var index = 0; index < sources.Count; ++index)
        Assert.That(
          _MeanAbsoluteError(sources[index].PixelData, pictures.AsSpan(index * frameBytes, frameBytes)),
          Is.LessThan(_TOLERANCE),
          $"ffmpeg's picture {index} is not the frame that was encoded");
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }
  }

  // ============================================================================================
  // Fixtures
  // ============================================================================================

  /// <summary>
  /// How far a picture may sit from its source before it is a different picture. Only the JPEG
  /// quantisation and the chroma round trip belong under it, and both are small: ffmpeg 8.1.2
  /// returns this clip at a mean absolute error of 1.1 interlaced and 1.9 progressive. The three
  /// ways this test could pass while being wrong measure far above it on this fixture — a field
  /// weave the wrong way round 134, a picture taken from the wrong place in the clip 25 — so the
  /// bar sits between them rather than merely above the noise.
  /// </summary>
  private const double _TOLERANCE = 6d;

  private static byte[] _Mux(int width, int height, List<RawImage> sources, out MediaStreamInfo description) {
    var encoder = AvidMeridienCompressedVideoEncoder.Create(_Stream(width, height));
    var packets = sources.Select((picture, index) => {
      Assert.That(encoder.TryEncode(picture, index, out var packet), Is.True);
      return packet;
    }).ToList();

    description = encoder.DescribeStream();
    return VideoIO.Mux<Mp4Writer>([description], packets);
  }

  private static List<RawImage> _Clip(int width, int height, int frames)
    => [.. Enumerable.Range(0, frames).Select(index => _Striped(width, height, index))];

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

  /// <summary>
  /// Even rows blue, odd rows red, in eight-pixel horizontal runs so 4:2:2 chroma costs nothing, over
  /// a green ramp that moves with the frame index.
  /// </summary>
  /// <remarks>
  /// Row parity is what makes a wrong field weave visible and the moving ramp is what makes a
  /// repeated or reordered picture visible. Eight-pixel runs keep the difference between a correct
  /// decode and a wrong one far larger than the codec's own loss.
  /// </remarks>
  private static RawImage _Striped(int width, int height, int frameIndex) {
    var pixels = new byte[checked(width * height * 3)];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      var ramp = (byte)(20 + ((x / 8 + frameIndex * 7) % 24) * 8);
      if ((y & 1) == 0) {
        pixels[at] = 20;
        pixels[at + 1] = ramp;
        pixels[at + 2] = 220;
      } else {
        pixels[at] = 220;
        pixels[at + 1] = ramp;
        pixels[at + 2] = 20;
      }
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };
  }

  private static double _MeanAbsoluteError(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual) {
    var count = Math.Min(expected.Length, actual.Length);
    var total = 0L;
    for (var index = 0; index < count; ++index)
      total += Math.Abs(expected[index] - actual[index]);

    return total / (double)count;
  }

  /// <summary>Returns the first component's sampling-factor byte out of a JPEG's SOF0 segment.</summary>
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
        return jpeg[position + 11];
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
