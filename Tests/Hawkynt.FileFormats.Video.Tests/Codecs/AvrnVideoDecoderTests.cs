using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;

namespace FileFormat.Codecs.Tests;

[TestFixture]
public class AvrnVideoDecoderTests {

  private static readonly CodecTag _Avrn = CodecTag.FromCharacters("AVRn");

  private static MediaStreamInfo _Stream(
    int width,
    int height,
    CodecTag? codec = null,
    ReadOnlyMemory<byte> privateData = default) => new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = codec ?? _Avrn,
      Width = width,
      Height = height,
      CodecPrivateData = privateData,
    };

  // A real 16x32 baseline JPEG (ffmpeg's own encoder, 4:4:4): top sixteen rows red, bottom sixteen blue.
  /// <summary>
  /// A real 16x32 baseline JPEG: the top half red, the bottom half blue.
  /// </summary>
  /// <remarks>
  /// It has to be a file a JPEG decoder will actually read. The fixture this replaced declared a
  /// Huffman table segment four bytes shorter than the tables inside it, so the marker walk ran into
  /// the middle of the next segment -- FFmpeg calls it "huffman table decode error" and refuses the
  /// frame outright. Three tests were asserting AVRn's behaviour on a file no decoder accepts, which
  /// says nothing about AVRn.
  /// </remarks>
  private static readonly byte[] _RedOverBlue = [
    0xFF, 0xD8, 0xFF, 0xFE, 0x00, 0x10, 0x4C, 0x61, 0x76, 0x63, 0x36, 0x32, 0x2E, 0x32, 0x38, 0x2E,
    0x31, 0x30, 0x32, 0x00, 0xFF, 0xDB, 0x00, 0x43, 0x00, 0x08, 0x04, 0x04, 0x04, 0x04, 0x04, 0x05,
    0x05, 0x05, 0x05, 0x05, 0x05, 0x06, 0x06, 0x06, 0x06, 0x06, 0x06, 0x06, 0x06, 0x06, 0x06, 0x06,
    0x06, 0x06, 0x07, 0x07, 0x07, 0x08, 0x08, 0x08, 0x07, 0x07, 0x07, 0x06, 0x06, 0x07, 0x07, 0x08,
    0x08, 0x08, 0x08, 0x09, 0x09, 0x09, 0x08, 0x08, 0x08, 0x08, 0x09, 0x09, 0x0A, 0x0A, 0x0A, 0x0C,
    0x0C, 0x0B, 0x0B, 0x0E, 0x0E, 0x0E, 0x11, 0x11, 0x14, 0xFF, 0xC4, 0x00, 0x4E, 0x00, 0x01, 0x01,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x06,
    0x01, 0x01, 0x01, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x08, 0x07, 0x06, 0x10, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x11, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x20,
    0x00, 0x10, 0x03, 0x01, 0x12, 0x00, 0x02, 0x12, 0x00, 0x03, 0x12, 0x00, 0xFF, 0xDA, 0x00, 0x0C,
    0x03, 0x01, 0x00, 0x02, 0x11, 0x03, 0x11, 0x00, 0x3F, 0x00, 0x8B, 0x1C, 0xA0, 0xDF, 0xC0, 0x00,
    0x48, 0x0A, 0xA8, 0x4D, 0x60, 0x00, 0x3F, 0xFF, 0xD9,
  ];

  [Test]
  [Category("Unit")]
  public void AcceptsTheAvrnTagIgnoringCase() {
    Assert.That(AvrnVideoDecoder.Accepts(_Stream(4, 2)), Is.True);
    Assert.That(AvrnVideoDecoder.Accepts(_Stream(4, 2, CodecTag.FromCharacters("avrN"))), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void RefusesAnythingElse()
    => Assert.That(AvrnVideoDecoder.Accepts(_Stream(4, 2, CodecTag.FromCharacters("MJPG"))), Is.False);

  [Test]
  [Category("Unit")]
  public void WithoutOneToOneMarkerAvrnIsMotionJpegAndKeepsBottomContainerRows() {
    var decoder = AvrnVideoDecoder.Create(_Stream(16, 16));

    Assert.That(decoder.TryDecode(new(0, _RedOverBlue), out var frame), Is.True);
    Assert.Multiple(() => {
      Assert.That(frame.Width, Is.EqualTo(16));
      Assert.That(frame.Height, Is.EqualTo(16));
      Assert.That(frame.PixelData[..3], Is.EqualTo(new byte[] { 0x00, 0x00, 0xFE }));
      Assert.That(frame.PixelData[^3..], Is.EqualTo(new byte[] { 0x00, 0x00, 0xFE }));
    });
  }

  [Test]
  [Category("Unit")]
  public void MotionJpegWithoutContainerGeometryKeepsTheWholeJpeg() {
    var decoder = AvrnVideoDecoder.Create(_Stream(0, 0));

    Assert.That(decoder.TryDecode(new(0, _RedOverBlue), out var frame), Is.True);
    Assert.Multiple(() => {
      Assert.That(frame.Width, Is.EqualTo(16));
      Assert.That(frame.Height, Is.EqualTo(32));
      Assert.That(frame.PixelData[..3], Is.EqualTo(new byte[] { 0xFE, 0x00, 0x00 }));
      Assert.That(frame.PixelData[^3..], Is.EqualTo(new byte[] { 0x00, 0x00, 0xFE }));
    });
  }

  [Test]
  [Category("Unit")]
  public void MotionJpegRefusesContainerGeometryLargerThanTheCodedPicture() {
    var decoder = AvrnVideoDecoder.Create(_Stream(16, 33));

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, _RedOverBlue), out _));
    Assert.That(failure!.Message, Does.Contain("larger than the 16x32"));
  }

  [Test]
  [Category("Unit")]
  public void OneToOneProgressiveIsUyvyAndSkipsWholeLeadingRows() {
    var decoder = AvrnVideoDecoder.Create(_Stream(4, 2, privateData: _OneToOneFormat()));
    byte[] packet = [
      // One unused coded row ahead of the picture.
      90, 91, 92, 93, 94, 95, 96, 97,
      // Picture row 0: Cb Y0 Cr Y1, twice.
      10, 20, 30, 40, 50, 60, 70, 80,
      // Picture row 1.
      11, 21, 31, 41, 51, 61, 71, 81,
      // A trailing partial row is ignored when deriving coded height.
      0xAA, 0xBB,
    ];

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.Multiple(() => {
      Assert.That(frame.Width, Is.EqualTo(4));
      Assert.That(frame.Height, Is.EqualTo(2));
      Assert.That(frame.Format, Is.EqualTo(PixelFormat.Yuv422P8));
      Assert.That(frame.PixelData, Is.EqualTo(new byte[] {
        20, 40, 60, 80, 21, 41, 61, 81,
        10, 50, 11, 51,
        30, 70, 31, 71,
      }));
    });
  }

  [Test]
  [Category("Unit")]
  public void OneToOneMarkerCanBeSuppliedAsCodecExtraDataWithoutBitmapHeader() {
    var format = _OneToOneFormat();
    var extra = format.AsMemory(40);
    var decoder = AvrnVideoDecoder.Create(_Stream(4, 1, privateData: extra));
    byte[] packet = [10, 20, 30, 40, 50, 60, 70, 80];

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] { 20, 40, 60, 80, 10, 50, 30, 70 }));
  }

  [Test]
  [Category("Unit")]
  public void OneToOneInterlacedReassemblesTheTwoFieldsAndHonorsTheFieldOrderByte() {
    var decoder = AvrnVideoDecoder.Create(_Stream(4, 4, privateData: _OneToOneFormat(interlaced: true, firstFieldOnOddRows: true)));
    byte[] packet = [
      // First field: rows A and B.
      10, 20, 30, 40, 50, 60, 70, 80,
      11, 21, 31, 41, 51, 61, 71, 81,
      // Four-byte field separator.
      0, 0, 0, 0,
      // Second field: rows C and D.
      12, 22, 32, 42, 52, 62, 72, 82,
      13, 23, 33, 43, 53, 63, 73, 83,
    ];

    Assert.That(decoder.TryDecode(new(0, packet), out var frame), Is.True);
    Assert.That(frame.PixelData, Is.EqualTo(new byte[] {
      // Luma rows C, A, D, B because the first field goes to odd output rows.
      22, 42, 62, 82,
      20, 40, 60, 80,
      23, 43, 63, 83,
      21, 41, 61, 81,
      // Cb planes in the same row order.
      12, 52, 10, 50, 13, 53, 11, 51,
      // Cr planes.
      32, 72, 30, 70, 33, 73, 31, 71,
    }));
  }

  [Test]
  [Category("Unit")]
  public void OneToOneRefusesAPacketShorterThanOnePicture() {
    var decoder = AvrnVideoDecoder.Create(_Stream(4, 2, privateData: _OneToOneFormat()));

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, new byte[15]), out _));
    Assert.That(failure!.Message, Does.Contain("needs at least 16"));
  }

  [Test]
  [Category("Unit")]
  public void InterlacedOneToOneRefusesAPacketWithoutTheSecondFieldsSeparatorBytes() {
    var decoder = AvrnVideoDecoder.Create(_Stream(4, 4, privateData: _OneToOneFormat(interlaced: true, firstFieldOnOddRows: false)));

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, new byte[32]), out _));
    Assert.That(failure!.Message, Does.Contain("require at least 36"));
  }

  private static byte[] _OneToOneFormat(bool interlaced = false, bool firstFieldOnOddRows = false) {
    var extraLength = interlaced ? 53 : 31;
    var format = new byte[40 + extraLength];
    BinaryPrimitives.WriteUInt32LittleEndian(format, 40);
    var extra = format.AsSpan(40);

    if (!interlaced) {
      "1:1"u8.CopyTo(extra[28..]);
      return format;
    }

    extra[4] = 24; // descriptor begins at extra[4] + 4 == 28
    "1:1("u8.CopyTo(extra[28..]);
    extra[52] = firstFieldOnOddRows ? (byte)1 : (byte)0;
    return format;
  }
}
