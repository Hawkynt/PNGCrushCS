using System;
using System.Collections.Generic;
using System.Linq;
using FileFormat.Codecs;
using FileFormat.Core;

namespace Hawkynt.FileFormats.Video.Tests.Codecs;

/// <summary>
/// Which streams the Motion JPEG decoder takes, and — the part worth a fixture — which ones it does
/// not.
/// </summary>
/// <remarks>
/// ffmpeg has one <c>mjpeg</c> decoder where this library has several codecs, so its four-character
/// code table folds lossless JPEG and QuickTime's Motion JPEG-A in beside plain Motion JPEG. Copying
/// that table would be the one mistake worse than refusing a file: both of those decode to something
/// picture-shaped here, and nobody checks a picture that looks like a picture. The fixture below
/// builds the Motion JPEG-A case and shows what that something is.
/// </remarks>
[TestFixture]
public sealed class MotionJpegDecoderTests {

  [TestCase("MJPG", true)]
  [TestCase("mjpg", true)]
  [TestCase("jpeg", true)]
  [TestCase("LJPG", false, TestName = "lossless JPEG, a predictive coding this reads none of")]
  [TestCase("MJPA", false, TestName = "QuickTime Motion JPEG-A, whose packet is two field pictures")]
  [TestCase("MJPB", false, TestName = "Motion JPEG-B, which has a decoder of its own")]
  [TestCase("MPG2", false)]
  [Category("Unit")]
  public void TheCodecTakesTheStreamsItsContainersName(string tag, bool expected)
    => Assert.That(MotionJpegDecoder.Accepts(_Stream(tag)), Is.EqualTo(expected));

  [Test]
  [Category("Unit")]
  public void TheMatroskaNameIsTakenAndAnAudioStreamIsNotWhateverItsTag() {
    Assert.Multiple(() => {
      Assert.That(
        MotionJpegDecoder.Accepts(new() { Index = 0, Kind = MediaStreamKind.Video, CodecId = "V_MJPEG" }),
        Is.True);
      Assert.That(
        MotionJpegDecoder.Accepts(new() { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("MJPG") }),
        Is.False);
    });
  }

  /// <summary>
  /// Why <c>MJPA</c> is not a fourth spelling of this codec: its packet decodes, and decodes wrong.
  /// </summary>
  /// <remarks>
  /// A Motion JPEG-A frame is two field pictures, each a complete JPEG of half the frame's height,
  /// each behind an <c>APP1</c> marker beginning <c>mjpg</c> that states the field's size and where
  /// its own headers and data begin. This decoder reads a packet as one whole JPEG, so it stops at
  /// the first <c>EOI</c> and hands back the first field — half the frame, at full width, with
  /// nothing raised anywhere. Were the tag claimed, that is what every file carrying it would decode
  /// to, so this test states the failure rather than the refusal: it is the reason the refusal is
  /// right, and it will still be true for whoever writes the decoder Motion JPEG-A actually needs.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void AMotionJpegAFrameWouldDecodeToOneOfItsTwoFields() {
    const int WIDTH = 32;
    const int FRAME_HEIGHT = 16;
    const int FIELD_HEIGHT = FRAME_HEIGHT / 2;

    var top = _FieldJpeg(WIDTH, FIELD_HEIGHT, 96);
    var bottom = _FieldJpeg(WIDTH, FIELD_HEIGHT, 160);
    var frame = _MotionJpegAFrame(top, bottom);

    var decoder = MotionJpegDecoder.Create(_Stream("MJPG"));

    Assert.That(decoder.TryDecode(new(0, frame), out var picture), Is.True,
      "The packet decodes rather than failing, which is exactly the problem.");
    Assert.Multiple(() => {
      Assert.That(picture.Width, Is.EqualTo(WIDTH));
      Assert.That(picture.Height, Is.EqualTo(FIELD_HEIGHT), "half the frame, and no error to say so");
    });
  }

  // ============================================================================================
  // Building a Motion JPEG-A frame
  // ============================================================================================

  /// <summary>One field of a frame, as the complete baseline JPEG a Motion JPEG-A encoder writes.</summary>
  private static byte[] _FieldJpeg(int width, int height, byte luminance) {
    var encoder = MotionJpegVideoEncoder.Create(new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Width = width,
      Height = height,
      TimeBase = new(1, 25),
      FrameRate = new(25, 1),
    });

    var image = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Gray8,
      PixelData = Enumerable.Repeat(luminance, width * height).ToArray(),
    };

    Assert.That(encoder.TryEncode(image, 0, out var packet), Is.True);
    return packet.Data.ToArray();
  }

  /// <summary>The two fields spliced into one packet, each behind its <c>APP1</c> field header.</summary>
  private static byte[] _MotionJpegAFrame(byte[] top, byte[] bottom) {
    var first = _WithFieldHeader(top, isFirst: true);
    var second = _WithFieldHeader(bottom, isFirst: false);
    return [.. first, .. second];
  }

  /// <summary>
  /// One field with the <c>APP1</c> marker QuickTime identifies a Motion JPEG-A field by.
  /// </summary>
  /// <remarks>
  /// Four bytes of <c>mjpg</c> and then eight big-endian longs: the field's size, its size padded,
  /// the offset of the next field, and the offsets of the field's own quantisation, Huffman, frame
  /// and scan headers and of its entropy-coded data. Every offset is from the start of the field,
  /// so the ones read out of the JPEG have the marker's own length added to them.
  /// </remarks>
  private static byte[] _WithFieldHeader(byte[] jpeg, bool isFirst) {
    const int BODY = 4 + 8 * 4;
    const int MARKER = 2 + 2 + BODY;

    var offsets = _MarkerOffsets(jpeg);
    var fieldSize = jpeg.Length + MARKER;
    var header = new List<byte> { 0xFF, 0xE1, (BODY + 2) >> 8, (BODY + 2) & 0xFF };
    header.AddRange("mjpg"u8.ToArray());

    foreach (var value in new[] {
               fieldSize, fieldSize, isFirst ? fieldSize : 0,
               offsets.Quantisation + MARKER, offsets.Huffman + MARKER,
               offsets.Frame + MARKER, offsets.Scan + MARKER, offsets.Data + MARKER,
             })
      header.AddRange([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);

    // After the SOI, which stays where it is: a field is still a JPEG.
    return [.. jpeg[..2], .. header, .. jpeg[2..]];
  }

  /// <summary>Where a baseline JPEG's headers and entropy-coded data begin.</summary>
  private static (int Quantisation, int Huffman, int Frame, int Scan, int Data) _MarkerOffsets(byte[] jpeg) {
    int quantisation = 0, huffman = 0, frame = 0, scan = 0, data = 0;

    for (var i = 2; i < jpeg.Length - 3;) {
      if (jpeg[i] != 0xFF) {
        ++i;
        continue;
      }

      var marker = jpeg[i + 1];
      var length = (jpeg[i + 2] << 8) | jpeg[i + 3];

      switch (marker) {
        case 0xDB when quantisation == 0: quantisation = i; break;
        case 0xC4 when huffman == 0: huffman = i; break;
        case 0xC0: frame = i; break;
        case 0xDA:
          scan = i;
          data = i + 2 + length;
          return (quantisation, huffman, frame, scan, data);
      }

      i += 2 + length;
    }

    throw new InvalidOperationException("The encoder wrote a JPEG with no scan header in it.");
  }

  private static MediaStreamInfo _Stream(string tag)
    => new() { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters(tag) };
}
