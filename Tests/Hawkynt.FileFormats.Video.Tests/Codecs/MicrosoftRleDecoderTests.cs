using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Avi.Tests;
using FileFormat.Core;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Codecs.Tests;

/// <summary>
/// The Microsoft RLE video decoder, on streams built here byte by byte.
/// </summary>
/// <remarks>
/// The coding is lossless, so the arithmetic was settled against ffmpeg rather than here: every
/// frame of every stream measured decoded to the same picture ffmpeg decodes it to, sample for
/// sample. What these tests add is what that comparison cannot reach — the refusals, which no valid
/// stream produces, and the four-bit variant, which ffmpeg's own encoder will not write.
/// <para/>
/// The expected pictures are worked out from the opcodes rather than recorded from a run, so where
/// one of these numbers disagrees with the decoder, the comment beside it says which of the two is
/// wrong.
/// </remarks>
[TestFixture]
public sealed class MicrosoftRleDecoderTests {

  private const int _BI_RLE8 = 1;
  private const int _BI_RLE4 = 2;

  // ============================================================================================
  // Which streams it takes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ARunLengthCompressionIsTakenAtEitherDepth() {
    Assert.That(MicrosoftRleDecoder.Accepts(_Stream(compression: _BI_RLE8, bitsPerPixel: 8)), Is.True);
    Assert.That(MicrosoftRleDecoder.Accepts(_Stream(compression: _BI_RLE4, bitsPerPixel: 4)), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void TheFourCharacterCodeIsTakenInEitherSpelling() {
    var upper = _Stream(compression: _BI_RLE8, bitsPerPixel: 8);
    upper = _Retagged(upper, CodecTag.FromCharacters("MRLE"));
    Assert.That(MicrosoftRleDecoder.Accepts(upper), Is.True);

    var lower = _Retagged(_Stream(compression: _BI_RLE8, bitsPerPixel: 8), CodecTag.FromCharacters("mrle"));
    Assert.That(MicrosoftRleDecoder.Accepts(lower), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void AnUncompressedStreamIsNotTaken() {
    // BI_RGB is compression zero, which is the uncompressed codec's stream and not this one's. A
    // decoder claiming it would read a raster as opcodes.
    Assert.That(MicrosoftRleDecoder.Accepts(_Stream(compression: 0, bitsPerPixel: 8)), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void ASoundStreamIsNotTaken() {
    var sound = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Audio, Codec = new(_BI_RLE8) };

    Assert.That(MicrosoftRleDecoder.Accepts(sound), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheCodecIsRegistered() {
    var stream = _Stream(compression: _BI_RLE8, bitsPerPixel: 8);

    Assert.That(VideoFormatRegistry.AllCodecs.Select(c => c.CodecName), Does.Contain("Microsoft RLE"));
    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(stream), Is.InstanceOf<MicrosoftRleDecoder>());
  }

  // ============================================================================================
  // The opcodes
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AnEncodedRunFillsTheRowItStartsOn() {
    // Four pixels of index 3, then four of index 5, then the row ends. Rows are coded bottom-up, so
    // the row written first is the last row of the picture.
    var frame = _DecodeOne(
      _Stream(compression: _BI_RLE8, bitsPerPixel: 8, width: 8, height: 2),
      [4, 3, 4, 5, 0, 0, 8, 1, 0, 0, 0, 1]);

    Assert.That(frame.Format, Is.EqualTo(PixelFormat.Indexed8));
    Assert.That(_Row(frame, 1), Is.EqualTo(new byte[] { 3, 3, 3, 3, 5, 5, 5, 5 }));
    Assert.That(_Row(frame, 0), Is.EqualTo(new byte[] { 1, 1, 1, 1, 1, 1, 1, 1 }));
  }

  [Test]
  [Category("Unit")]
  public void AnAbsoluteRunSpellsItsPixelsOutAndIsPaddedToAWord() {
    // 00 05 introduces five literal pixels; five bytes is odd, so a sixth pads it out to a word. A
    // reader that skipped only the five would read the pad byte as the next opcode's count.
    var frame = _DecodeOne(
      _Stream(compression: _BI_RLE8, bitsPerPixel: 8, width: 8, height: 1),
      [0, 5, 9, 8, 7, 6, 5, 0, 3, 4, 0, 1]);

    Assert.That(_Row(frame, 0), Is.EqualTo(new byte[] { 9, 8, 7, 6, 5, 4, 4, 4 }));
  }

  [Test]
  [Category("Unit")]
  public void AFourBitRunAlternatesTheTwoNibblesHighOneFirst() {
    var frame = _DecodeOne(
      _Stream(compression: _BI_RLE4, bitsPerPixel: 4, width: 6, height: 1, paletteEntries: 16),
      [6, 0xAB, 0, 1]);

    Assert.That(_Row(frame, 0), Is.EqualTo(new byte[] { 0xA, 0xB, 0xA, 0xB, 0xA, 0xB }));
  }

  [Test]
  [Category("Unit")]
  public void AFourBitAbsoluteRunIsPaddedToAWordAndNotMerelyToAByte() {
    // Five pixels at four bits is three bytes, and three bytes is padded out to four. Rounding only
    // the nibbles to a byte leaves the reader half a byte out of step for the rest of the picture,
    // which is why the pixels after this one are checked as well as the run itself.
    var frame = _DecodeOne(
      _Stream(compression: _BI_RLE4, bitsPerPixel: 4, width: 8, height: 1, paletteEntries: 16),
      [0, 5, 0x12, 0x34, 0x50, 0x00, 3, 0x77, 0, 1]);

    Assert.That(_Row(frame, 0), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 7, 7, 7 }));
  }

  [Test]
  [Category("Unit")]
  public void EndOfBitmapStopsTheWalkBeforeWhateverFollowsIt() {
    var frame = _DecodeOne(
      _Stream(compression: _BI_RLE8, bitsPerPixel: 8, width: 4, height: 1),
      [4, 6, 0, 1, 4, 9]);

    Assert.That(_Row(frame, 0), Is.EqualTo(new byte[] { 6, 6, 6, 6 }));
  }

  // ============================================================================================
  // What makes it a video codec rather than a bitmap reader: the frame before
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ADeltaFrameLeavesEveryPixelItDoesNotNameAsTheFrameBeforeLeftIt() {
    // The whole of the inter-frame coding is here. The second frame moves the pen two columns along
    // the bottom row and paints two pixels; everything else must still be the first frame's. A
    // decoder starting each frame from an empty canvas decodes every opcode of this correctly and
    // still returns a picture that is almost entirely palette entry zero.
    var frames = _Decode(
      _Stream(compression: _BI_RLE8, bitsPerPixel: 8, width: 4, height: 2),
      [
        [4, 7, 0, 0, 4, 2, 0, 1],
        [0, 2, 2, 0, 2, 9, 0, 1],
      ]);

    Assert.That(frames.Count, Is.EqualTo(2));
    Assert.That(_Row(frames[0], 1), Is.EqualTo(new byte[] { 7, 7, 7, 7 }));
    Assert.That(_Row(frames[0], 0), Is.EqualTo(new byte[] { 2, 2, 2, 2 }));

    Assert.That(_Row(frames[1], 1), Is.EqualTo(new byte[] { 7, 7, 9, 9 }), "the two pixels the delta names");
    Assert.That(_Row(frames[1], 0), Is.EqualTo(new byte[] { 2, 2, 2, 2 }), "the row it says nothing about");
  }

  [Test]
  [Category("Unit")]
  public void EndOfLineLeavesTheRestOfTheRowAsTheFrameBeforeLeftIt() {
    var frames = _Decode(
      _Stream(compression: _BI_RLE8, bitsPerPixel: 8, width: 4, height: 1),
      [
        [4, 7, 0, 1],
        [2, 3, 0, 0, 0, 1],
      ]);

    Assert.That(_Row(frames[1], 0), Is.EqualTo(new byte[] { 3, 3, 7, 7 }));
  }

  [Test]
  [Category("Unit")]
  public void EachFrameIsItsOwnPictureAndNotAViewOfTheCanvas() {
    // The canvas is painted on again by the next frame. A decoder handing out the canvas itself
    // would leave a caller holding several frames with every one of them showing the last.
    var frames = _Decode(
      _Stream(compression: _BI_RLE8, bitsPerPixel: 8, width: 4, height: 1),
      [
        [4, 7, 0, 1],
        [4, 8, 0, 1],
      ]);

    Assert.That(_Row(frames[0], 0), Is.EqualTo(new byte[] { 7, 7, 7, 7 }));
    Assert.That(_Row(frames[1], 0), Is.EqualTo(new byte[] { 8, 8, 8, 8 }));
  }

  // ============================================================================================
  // The palette
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ThePaletteIsReadOutOfTheStreamFormatWithItsChannelsPutBack() {
    // The entries are BGRx quads and a RawImage's palette is RGB triplets, so the outer two swap.
    var stream = _Stream(compression: _BI_RLE8, bitsPerPixel: 8, width: 1, height: 1, paletteEntries: 4);
    var frame = _DecodeOne(stream, [1, 2, 0, 1]);

    Assert.That(frame.PaletteCount, Is.EqualTo(4));
    // Entry 2 is written below as B=2*4, G=2*4+1, R=2*4+2 — that is 8, 9, 10.
    Assert.That(frame.Palette![6], Is.EqualTo(10));
    Assert.That(frame.Palette[7], Is.EqualTo(9));
    Assert.That(frame.Palette[8], Is.EqualTo(8));
  }

  // ============================================================================================
  // The refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ADepthTheCodingIsNotDefinedAtIsRefusedByName() {
    var stream = _Stream(compression: _BI_RLE8, bitsPerPixel: 24, paletteEntries: 0);

    var failure = Assert.Throws<NotSupportedException>(() => MicrosoftRleDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("24 bits per pixel"));
  }

  [Test]
  [Category("Unit")]
  public void ADepthDisagreeingWithTheStatedCompressionIsRefusedRatherThanGuessedAt() {
    // Eight-bit compression beside a four-bit depth cannot be read either way round: as eight-bit
    // opcodes the picture comes out twice as wide as the header says, and as four-bit ones the
    // counts are wrong from the first run.
    var stream = _Stream(compression: _BI_RLE8, bitsPerPixel: 4, paletteEntries: 16);

    var failure = Assert.Throws<InvalidDataException>(() => MicrosoftRleDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("disagree"));
  }

  [Test]
  [Category("Unit")]
  public void AStreamWithNoPaletteIsRefusedRatherThanDecodedToGrey() {
    var stream = _Stream(compression: _BI_RLE8, bitsPerPixel: 8, paletteEntries: 0);

    var failure = Assert.Throws<InvalidOperationException>(() => MicrosoftRleDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("no palette"));
  }

  [Test]
  [Category("Unit")]
  public void APaletteShorterThanTheHeaderPromisesIsRefused() {
    var stream = _Stream(compression: _BI_RLE8, bitsPerPixel: 8, paletteEntries: 4);
    var truncated = new byte[stream.CodecPrivateData.Length - 4];
    stream.CodecPrivateData.Span[..truncated.Length].CopyTo(truncated);

    var failure = Assert.Throws<InvalidDataException>(
      () => MicrosoftRleDecoder.Create(_WithFormat(stream, truncated)));
    Assert.That(failure!.Message, Does.Contain("4 palette entries and carries 3"));
  }

  [Test]
  [Category("Unit")]
  public void RowsTheWrongWayUpAreRefusedRatherThanFlipped() {
    var stream = _Stream(compression: _BI_RLE8, bitsPerPixel: 8, height: -4);

    var failure = Assert.Throws<NotSupportedException>(() => MicrosoftRleDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("bottom-up"));
  }

  [Test]
  [Category("Unit")]
  public void AStreamFormatShorterThanABitmapHeaderIsRefused() {
    var stream = _WithFormat(_Stream(compression: _BI_RLE8, bitsPerPixel: 8), new byte[20]);

    var failure = Assert.Throws<InvalidOperationException>(() => MicrosoftRleDecoder.Create(stream));
    Assert.That(failure!.Message, Does.Contain("BITMAPINFOHEADER"));
  }

  [Test]
  [Category("Unit")]
  public void ARunThatCrossesTheEndOfItsRowIsRefusedRatherThanClipped() {
    // Clipping would return a picture that looks decoded and is one run short of the coding, and
    // would leave the pen where the stream did not put it for everything after.
    var failure = Assert.Throws<InvalidDataException>(
      () => _DecodeOne(_Stream(compression: _BI_RLE8, bitsPerPixel: 8, width: 4, height: 1), [6, 3, 0, 1]));

    Assert.That(failure!.Message, Does.Contain("does not fit a 4x1 picture"));
  }

  [Test]
  [Category("Unit")]
  public void AnAbsoluteRunLongerThanTheDataIsRefused() {
    var failure = Assert.Throws<InvalidDataException>(
      () => _DecodeOne(_Stream(compression: _BI_RLE8, bitsPerPixel: 8, width: 8, height: 1), [0, 6, 1, 2, 3]));

    Assert.That(failure!.Message, Does.Contain("needs 6 byte(s) and only 3 remain"));
  }

  [Test]
  [Category("Unit")]
  public void ADeltaLandingOutsideThePictureIsRefused() {
    var failure = Assert.Throws<InvalidDataException>(
      () => _DecodeOne(_Stream(compression: _BI_RLE8, bitsPerPixel: 8, width: 4, height: 2), [0, 2, 0, 9, 0, 1]));

    Assert.That(failure!.Message, Does.Contain("outside a 4x2 picture"));
  }

  [Test]
  [Category("Unit")]
  public void DataEndingOnHalfAnOpcodeIsRefused() {
    var failure = Assert.Throws<InvalidDataException>(
      () => _DecodeOne(_Stream(compression: _BI_RLE8, bitsPerPixel: 8, width: 4, height: 1), [4, 3, 7]));

    Assert.That(failure!.Message, Does.Contain("single byte"));
  }

  [Test]
  [Category("Unit")]
  public void AStreamThatSimplyRunsOutIsNotAnError() {
    // The end-of-bitmap escape is conventional rather than required, and a frame that paints every
    // pixel it means to and stops has nothing wrong with it.
    var frame = _DecodeOne(_Stream(compression: _BI_RLE8, bitsPerPixel: 8, width: 4, height: 1), [4, 3]);

    Assert.That(_Row(frame, 0), Is.EqualTo(new byte[] { 3, 3, 3, 3 }));
  }

  // ============================================================================================
  // Through a container, end to end
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AnAviOfRunLengthFramesDecodesThroughTheRegistry() {
    var palette = new byte[4 * 4];
    for (var entry = 0; entry < 4; ++entry) {
      palette[entry * 4] = (byte)(entry * 4);
      palette[entry * 4 + 1] = (byte)(entry * 4 + 1);
      palette[entry * 4 + 2] = (byte)(entry * 4 + 2);
    }

    // biCompression is a number here and not a code, which is how ffmpeg's AVI muxer writes it.
    var container = AviTestContainer.Build(
      "\u0001\0\0\0", 4, 2, 8,
      [
        [4, 1, 0, 0, 4, 2, 0, 1],
        [0, 2, 1, 0, 2, 3, 0, 1],
      ],
      palette);

    var frames = VideoFormatRegistry.DecodeFrames(container).Select(f => f.Image).ToList();

    Assert.That(frames.Count, Is.EqualTo(2));
    Assert.That(_Row(frames[0], 1), Is.EqualTo(new byte[] { 1, 1, 1, 1 }));
    Assert.That(_Row(frames[1], 1), Is.EqualTo(new byte[] { 1, 3, 3, 1 }));
    Assert.That(_Row(frames[1], 0), Is.EqualTo(new byte[] { 2, 2, 2, 2 }));
  }

  // ============================================================================================
  // Fixtures
  // ============================================================================================

  private static MediaStreamInfo _Stream(
    int compression,
    int bitsPerPixel,
    int width = 4,
    int height = 4,
    int paletteEntries = -1) {
    if (paletteEntries < 0)
      paletteEntries = bitsPerPixel == 4 ? 16 : 256;

    var format = new byte[40 + paletteEntries * 4];
    var span = format.AsSpan();
    BinaryPrimitives.WriteInt32LittleEndian(span, 40);
    BinaryPrimitives.WriteInt32LittleEndian(span[4..], width);
    BinaryPrimitives.WriteInt32LittleEndian(span[8..], height);
    BinaryPrimitives.WriteInt16LittleEndian(span[12..], 1);
    BinaryPrimitives.WriteInt16LittleEndian(span[14..], (short)bitsPerPixel);
    BinaryPrimitives.WriteInt32LittleEndian(span[16..], compression);
    BinaryPrimitives.WriteInt32LittleEndian(span[32..], paletteEntries);

    for (var entry = 0; entry < paletteEntries; ++entry) {
      format[40 + entry * 4] = (byte)(entry * 4);
      format[40 + entry * 4 + 1] = (byte)(entry * 4 + 1);
      format[40 + entry * 4 + 2] = (byte)(entry * 4 + 2);
    }

    return new() {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = new((uint)compression),
      Width = width,
      Height = Math.Abs(height),
      BitsPerPixel = bitsPerPixel,
      CodecPrivateData = format,
    };
  }

  private static MediaStreamInfo _Retagged(MediaStreamInfo stream, CodecTag codec) => new() {
    Index = stream.Index,
    Kind = stream.Kind,
    Codec = codec,
    Width = stream.Width,
    Height = stream.Height,
    BitsPerPixel = stream.BitsPerPixel,
    CodecPrivateData = stream.CodecPrivateData,
  };

  private static MediaStreamInfo _WithFormat(MediaStreamInfo stream, byte[] format) => new() {
    Index = stream.Index,
    Kind = stream.Kind,
    Codec = stream.Codec,
    Width = stream.Width,
    Height = stream.Height,
    BitsPerPixel = stream.BitsPerPixel,
    CodecPrivateData = format,
  };

  private static RawImage _DecodeOne(MediaStreamInfo stream, byte[] frame) => _Decode(stream, [frame])[0];

  private static IReadOnlyList<RawImage> _Decode(MediaStreamInfo stream, IReadOnlyList<byte[]> frames) {
    var decoder = MicrosoftRleDecoder.Create(stream);
    var pictures = new List<RawImage>(frames.Count);

    foreach (var frame in frames)
      if (decoder.TryDecode(new(0, frame), out var picture))
        pictures.Add(picture);

    return pictures;
  }

  /// <summary>One row of a picture, counted from the top the way it will be drawn.</summary>
  private static byte[] _Row(RawImage picture, int row)
    => picture.PixelData.AsSpan(row * picture.Width, picture.Width).ToArray();
}
