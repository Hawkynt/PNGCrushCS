using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using FileFormat.Core;
using FileFormat.Jpeg;
using Hawkynt.FileFormats.Video;

namespace FileFormat.Avi.Tests;

/// <summary>
/// The AVI reader's behaviour, carried over from when the reader lived in the image package and
/// decoded as it demuxed.
/// </summary>
/// <remarks>
/// Every measured behaviour is still asserted here; what moved is where some of them are observed.
/// A refusal by four-character code, by unrenderable depth, or of a frame chunk too short for its
/// raster used to come out of the call that opened the file, because that call also decided how to
/// decode it. The container no longer decides anything about codecs, so those three now come out of
/// the call that asks for a decoder or a picture — same exception types, same wording, one step
/// later. A container full of a codec nothing here reads is now demuxable, which is the point of
/// having moved them.
/// </remarks>
[TestFixture]
public sealed class AviReaderTests {

  private const int _WIDTH = AviTestContainer.FRAME_WIDTH;
  private const int _HEIGHT = AviTestContainer.FRAME_HEIGHT;

  private static readonly (byte B, byte G, byte R)[] _RowColours = [
    (0x00, 0x00, 0xFF), // red
    (0x00, 0xFF, 0x00), // green
    (0xFF, 0x00, 0x00), // blue
    (0x20, 0x40, 0x60),
  ];

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => AviReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromSpan_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => AviReader.FromBytes(new byte[8]));

  [Test]
  [Category("Unit")]
  public void FromSpan_NotAnAviFormType_ThrowsInvalidDataException() {
    var wave = AviTestContainer.Build("MJPG", _WIDTH, _HEIGHT, 24, [_Jpeg(0)]);
    // Replace the 'AVI ' form type with 'WAVE' — same container, different file.
    wave[8] = (byte)'W';
    wave[9] = (byte)'A';
    wave[10] = (byte)'V';
    wave[11] = (byte)'E';

    Assert.Throws<InvalidDataException>(() => AviReader.FromBytes(wave));
  }

  [Test]
  [Category("Unit")]
  public void NoVideoStream_IsRefusedWhenPicturesAreAskedFor() {
    // The demuxer takes it: an AVI of nothing but sound is a valid AVI, and remuxing it needs no
    // pictures. Asking it for pictures is what fails, which is where the refusal now lives.
    var audioOnly = AviTestContainer.Build("MJPG", _WIDTH, _HEIGHT, 24, [_Jpeg(0)], streamType: "auds");

    var container = AviReader.FromBytes(audioOnly);
    Assert.That(AviContainer.Streams(container).Any(s => s.Kind == MediaStreamKind.Video), Is.False);
    Assert.Throws<InvalidDataException>(() => VideoFormatRegistry.DecodeFrames(audioOnly).ToList());
  }

  [Test]
  [Category("Unit")]
  public void MotionJpeg_FrameCount_IsTheNumberOfFrameChunks()
    => Assert.That(_Frames(AviTestContainer.Build("MJPG", _WIDTH, _HEIGHT, 24, [_Jpeg(0), _Jpeg(1), _Jpeg(2)])).Count, Is.EqualTo(3));

  [Test]
  [Category("Unit")]
  public void MotionJpeg_LowercaseFourCC_IsAlsoMotionJpeg() {
    // ffprobe reads a container whose biCompression is 'mjpg' as mjpeg, three frames, exactly as it
    // reads the uppercase one — both spellings are the same codec.
    Assert.That(_Frames(AviTestContainer.Build("mjpg", _WIDTH, _HEIGHT, 24, [_Jpeg(0), _Jpeg(1)])).Count, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void MotionJpeg_EachFrameEqualsTheSameJpegDecodedOnItsOwn() {
    var jpegs = new[] { _Jpeg(0), _Jpeg(1), _Jpeg(2) };
    var frames = _Frames(AviTestContainer.Build("MJPG", _WIDTH, _HEIGHT, 24, jpegs));

    for (var index = 0; index < jpegs.Length; ++index) {
      var direct = JpegFile.ToRawImage(JpegReader.FromBytes(jpegs[index]));

      Assert.That(frames[index].Width, Is.EqualTo(direct.Width), $"frame {index} width");
      Assert.That(frames[index].Height, Is.EqualTo(direct.Height), $"frame {index} height");
      Assert.That(frames[index].ToRgb24(), Is.EqualTo(direct.ToRgb24()), $"frame {index} pixels");
    }
  }

  [Test]
  [Category("Unit")]
  public void MotionJpeg_FramesAreReturnedInTheOrderTheyWereWritten() {
    // Each frame is a different picture, so a reader handing back the wrong one — or the same one
    // three times — fails here rather than passing on a container whose frames all look alike.
    var frames = _Frames(AviTestContainer.Build("MJPG", _WIDTH, _HEIGHT, 24, [_Jpeg(0), _Jpeg(1), _Jpeg(2)]));

    Assert.That(frames[0].ToRgb24(), Is.Not.EqualTo(frames[1].ToRgb24()));
    Assert.That(frames[1].ToRgb24(), Is.Not.EqualTo(frames[2].ToRgb24()));
  }

  [Test]
  [Category("Unit")]
  public void EmptyFrameChunk_IsNotCountedAsAFrame() {
    // Measured against the oracle: an AVI of four '00dc' chunks one of which is zero-length is
    // reported by `ffprobe -count_frames` as three frames. An empty chunk carries no picture and
    // ffmpeg does not invent one for it, so neither does this.
    var container = AviTestContainer.Build("MJPG", _WIDTH, _HEIGHT, 24, [_Jpeg(0), [], _Jpeg(1), _Jpeg(2)]);

    Assert.That(_Frames(container).Count, Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void Uncompressed_BottomUpRows_AreFlippedIntoPictureOrder() {
    var raster = AviTestContainer.BuildBgr24Raster(_WIDTH, _HEIGHT, _RowColours, bottomUp: true);

    _AssertRowColours(_Frames(AviTestContainer.Build("\0\0\0\0", _WIDTH, _HEIGHT, 24, [raster]))[0]);
  }

  [Test]
  [Category("Unit")]
  public void Uncompressed_NegativeHeightRunsTopDown() {
    // ffmpeg writes bgr24 rawvideo with biHeight = -37, i.e. top-down, so both signs have to land on
    // the same picture.
    var raster = AviTestContainer.BuildBgr24Raster(_WIDTH, _HEIGHT, _RowColours, bottomUp: false);

    _AssertRowColours(_Frames(AviTestContainer.Build("\0\0\0\0", _WIDTH, -_HEIGHT, 24, [raster]))[0]);
  }

  [Test]
  [Category("Unit")]
  public void Uncompressed_ReportsTheSizeFromTheStreamFormat() {
    var raster = AviTestContainer.BuildBgr24Raster(_WIDTH, _HEIGHT, _RowColours, bottomUp: true);
    var frames = _Frames(AviTestContainer.Build("\0\0\0\0", _WIDTH, _HEIGHT, 24, [raster, raster]));

    Assert.That(frames.Count, Is.EqualTo(2));
    Assert.That(frames[1].Width, Is.EqualTo(_WIDTH));
    Assert.That(frames[1].Height, Is.EqualTo(_HEIGHT));
  }

  [Test]
  [Category("Unit")]
  public void Uncompressed_EightBitFramesTakeTheirColoursFromTheStreamFormatPalette() {
    var palette = new byte[4 * 4];
    for (var i = 0; i < 4; ++i) {
      palette[i * 4] = _RowColours[i].B;
      palette[i * 4 + 1] = _RowColours[i].G;
      palette[i * 4 + 2] = _RowColours[i].R;
    }

    var raster = AviTestContainer.BuildIndexed8Raster(_WIDTH, _HEIGHT, bottomUp: true);

    _AssertRowColours(_Frames(AviTestContainer.Build("\0\0\0\0", _WIDTH, _HEIGHT, 8, [raster], palette))[0]);
  }

  [Test]
  [Category("Unit")]
  public void Uncompressed_ShortFrameChunk_IsRefused() {
    // Half a raster is not a picture. Padding it out would return a frame that is partly invented,
    // which is the one thing a reader must never do quietly.
    var raster = AviTestContainer.BuildBgr24Raster(_WIDTH, _HEIGHT, _RowColours, bottomUp: true);
    var container = AviTestContainer.Build("\0\0\0\0", _WIDTH, _HEIGHT, 24, [raster[..(raster.Length / 2)]]);

    Assert.Throws<InvalidDataException>(() => _Frames(container));
  }

  [TestCase("H264")]
  [TestCase("FMP4")]
  [TestCase("DIVX")]
  [TestCase("XVID")]
  [TestCase("DIB ")]
  [Category("Unit")]
  public void UnsupportedCodec_IsRefusedWithItsFourCharacterCode(string fourCC) {
    var container = AviTestContainer.Build(fourCC, 64, 48, 24, [new byte[64]]);

    var failure = Assert.Throws<NotSupportedException>(() => _Frames(container));
    Assert.That(failure!.Message, Does.Contain(fourCC));
  }

  [TestCase("H264")]
  [TestCase("XVID")]
  [Category("Unit")]
  public void UnsupportedCodec_StillDemuxes(string fourCC) {
    // The refusal is the codec's and not the container's. A file nothing here decodes still comes
    // apart into its packets, which is what a remux into another container would move — and what the
    // reader this replaced could never hand over, because the only thing it produced was pixels.
    var container = AviReader.FromBytes(AviTestContainer.Build(fourCC, 64, 48, 24, [new byte[64], new byte[64]]));

    var streams = AviContainer.Streams(container);
    Assert.That(streams, Has.Count.EqualTo(1));
    Assert.That(streams[0].Codec.ToString(), Is.EqualTo(fourCC));
    Assert.That(AviContainer.ReadPackets(container).Count(), Is.EqualTo(2));
    Assert.That(VideoFormatRegistry.CanDecode(streams[0]), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void Uncompressed_SixteenBitFrames_AreFiveFiveFive() {
    // Refused here while the bitmap reader behind this one read BI_RGB at 16 bits as 5-6-5, which
    // put 395 of 2257 pixels of a gradient wrong against ffmpeg. That reader now takes the layout
    // from the channel masks, so the frame comes back as the file states it and the refusal is gone.
    var raster = AviTestContainer.BuildRgb555Raster(_WIDTH, _HEIGHT, _RowColours, bottomUp: true);

    var rgb = _Frames(AviTestContainer.Build("\0\0\0\0", _WIDTH, _HEIGHT, 16, [raster]))[0].ToRgb24();
    for (var row = 0; row < _HEIGHT; ++row) {
      var offset = row * _WIDTH * 3;
      Assert.That(rgb[offset], Is.EqualTo(_ThroughFiveBits(_RowColours[row].R)), $"row {row} red");
      Assert.That(rgb[offset + 1], Is.EqualTo(_ThroughFiveBits(_RowColours[row].G)), $"row {row} green");
      Assert.That(rgb[offset + 2], Is.EqualTo(_ThroughFiveBits(_RowColours[row].B)), $"row {row} blue");
    }
  }

  [Test]
  [Category("Unit")]
  public void Uncompressed_ThirtyTwoBitFrames_KeepTheirAlpha() {
    // Refused here while a 32-bit BI_RGB bitmap came back from that reader as Indexed1 with no
    // palette and threw when asked for colours.
    byte[] alphaPerRow = [0x10, 0x40, 0x80, 0xFF];
    var raster = AviTestContainer.BuildBgra32Raster(_WIDTH, _HEIGHT, _RowColours, alphaPerRow, bottomUp: true);

    var image = _Frames(AviTestContainer.Build("\0\0\0\0", _WIDTH, _HEIGHT, 32, [raster]))[0];

    Assert.That(image.Format, Is.EqualTo(PixelFormat.Bgra32));
    for (var row = 0; row < _HEIGHT; ++row)
      Assert.That(image.PixelData[row * _WIDTH * 4 + 3], Is.EqualTo(alphaPerRow[row]), $"row {row} alpha");
  }

  [Test]
  [Category("Unit")]
  public void Uncompressed_ThirtyTwoBitFrames_WithNothingInTheFourthByte_AreOpaque() {
    // The fourth byte is padding as often as it is alpha, and a frame whose every one is zero is the
    // former; both tools render it opaque. Taking it literally would make the whole film invisible.
    byte[] noAlpha = [0x00, 0x00, 0x00, 0x00];
    var raster = AviTestContainer.BuildBgra32Raster(_WIDTH, _HEIGHT, _RowColours, noAlpha, bottomUp: true);

    _AssertRowColours(_Frames(AviTestContainer.Build("\0\0\0\0", _WIDTH, _HEIGHT, 32, [raster]))[0]);
  }

  [TestCase((short)2)]
  [TestCase((short)48)]
  [Category("Unit")]
  public void Uncompressed_DepthNoBitmapIsStoredAt_IsRefused(short bitsPerPixel) {
    var stride = (_WIDTH * bitsPerPixel / 8 + 3) & ~3;
    var container = AviTestContainer.Build("\0\0\0\0", _WIDTH, _HEIGHT, bitsPerPixel, [new byte[stride * _HEIGHT]]);

    var failure = Assert.Throws<NotSupportedException>(() => _Frames(container));
    Assert.That(failure!.Message, Does.Contain(bitsPerPixel.ToString()));
  }

  /// <summary>What a channel becomes once stored in five bits and widened back out.</summary>
  private static byte _ThroughFiveBits(byte value) {
    var stored = value >> 3;
    return (byte)((stored << 3) | (stored >> 2));
  }

  [Test]
  [Category("Unit")]
  public void StreamFormatShorterThanItStates_IsRefused() {
    // biSize larger than the chunk would send the bitmap reader looking for a palette past the end
    // of what the file holds. This one stays in the container: how long a strf chunk is against what
    // it claims is a question about the chunk, not about the codec inside it.
    var container = AviTestContainer.Build("\0\0\0\0", _WIDTH, _HEIGHT, 8, [new byte[_WIDTH * _HEIGHT]]);
    var strf = _FindChunk(container, "strf");
    // biSize is the first field of the stream format chunk.
    container[strf] = 200;

    Assert.Throws<InvalidDataException>(() => AviReader.FromBytes(container));
  }

  private static int _FindChunk(byte[] container, string id) {
    for (var i = 0; i + 8 < container.Length; ++i)
      if (container[i] == id[0] && container[i + 1] == id[1] && container[i + 2] == id[2] && container[i + 3] == id[3])
        return i + 8;

    throw new InvalidOperationException($"no '{id}' chunk in the built container");
  }

  [Test]
  [Category("Unit")]
  public void PastTheLastFrame_TheWalkSimplyEnds() {
    var container = AviTestContainer.Build("MJPG", _WIDTH, _HEIGHT, 24, [_Jpeg(0)]);

    Assert.That(VideoFormatRegistry.DecodeFrames(container).Count(), Is.EqualTo(1));
    Assert.That(VideoFormatRegistry.DecodeFrames(container).Skip(1), Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void EveryFrameIsReachable() {
    var container = AviTestContainer.Build("MJPG", _WIDTH, _HEIGHT, 24, [_Jpeg(0), _Jpeg(1)]);

    Assert.That(_Frames(container).Count, Is.EqualTo(2));
  }

  // ------------------------------------------------------------------------------------------
  // What the split between demuxing and decoding buys
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void OnlyTheFramesAskedFor_AreDecoded() {
    // The last frame chunk is half a raster, which throws when decoded. Taking the first frame must
    // still succeed: if the walk decoded eagerly — as the reader this replaced did, materialising
    // every frame at open — the broken one at the end would take the good one at the front with it.
    var raster = AviTestContainer.BuildBgr24Raster(_WIDTH, _HEIGHT, _RowColours, bottomUp: true);
    var container = AviTestContainer.Build("\0\0\0\0", _WIDTH, _HEIGHT, 24, [raster, raster[..(raster.Length / 2)]]);

    _AssertRowColours(VideoFormatRegistry.DecodeFrames(container).First().Image);
    Assert.Throws<InvalidDataException>(() => VideoFormatRegistry.DecodeFrames(container).ToList());
  }

  [Test]
  [Category("Unit")]
  public void PacketsAreWindowsOntoTheFileRatherThanCopies() {
    var raster = AviTestContainer.BuildBgr24Raster(_WIDTH, _HEIGHT, _RowColours, bottomUp: true);
    var bytes = AviTestContainer.Build("\0\0\0\0", _WIDTH, _HEIGHT, 24, [raster, raster]);
    var container = AviReader.FromBytes(bytes);

    Assert.That(AviContainer.ReadPackets(container).Select(p => p.Data.Length), Is.All.EqualTo(raster.Length));

    // The proof that nothing was copied: every packet is a slice of the very array the container was
    // opened over. A demuxer that kept its own copy of a film would double it.
    var payload = _FindChunk(bytes, "00dc");
    foreach (var packet in AviContainer.ReadPackets(container)) {
      Assert.That(MemoryMarshal.TryGetArray(packet.Data, out var segment), Is.True);
      Assert.That(segment.Array, Is.SameAs(bytes));
    }

    Assert.That(AviContainer.ReadPackets(container).First().Data.Span[0], Is.EqualTo(bytes[payload]));
  }

  [Test]
  [Category("Unit")]
  public void PacketsCarryTheirStreamAndTheirPositionInTime() {
    var container = AviReader.FromBytes(AviTestContainer.Build("MJPG", _WIDTH, _HEIGHT, 24, [_Jpeg(0), _Jpeg(1), _Jpeg(2)]));
    var packets = AviContainer.ReadPackets(container).ToList();

    Assert.That(packets.Select(p => p.StreamIndex), Is.All.Zero);
    Assert.That(packets.Select(p => p.PresentationTimestamp), Is.EqualTo(new long?[] { 0, 1, 2 }));

    // dwScale 1 over dwRate 10 — one packet is a tenth of a second, and the third is due at 0.2 s.
    var stream = AviContainer.Streams(container)[0];
    Assert.That(stream.TimeBase, Is.EqualTo(new Rational(1, 10)));
    Assert.That(stream.FrameRate, Is.EqualTo(new Rational(10, 1)));
    Assert.That(stream.TimeBase.Scale(packets[2].PresentationTimestamp!.Value), Is.EqualTo(TimeSpan.FromSeconds(0.2)));
  }

  [Test]
  [Category("Unit")]
  public void TheStreamCarriesItsCodecPrivateDataAcrossUntouched() {
    // The strf chunk goes to the decoder verbatim. It is what the raw-video decoder reads its layout
    // out of, and what a muxer would have to write again to produce a file that still decodes.
    var raster = AviTestContainer.BuildBgr24Raster(_WIDTH, _HEIGHT, _RowColours, bottomUp: true);
    var container = AviReader.FromBytes(AviTestContainer.Build("\0\0\0\0", _WIDTH, _HEIGHT, 24, [raster]));

    var stream = AviContainer.Streams(container)[0];
    Assert.That(stream.CodecPrivateData.Length, Is.EqualTo(40));
    Assert.That(stream.Width, Is.EqualTo(_WIDTH));
    Assert.That(stream.Height, Is.EqualTo(_HEIGHT));
    Assert.That(stream.BitsPerPixel, Is.EqualTo(24));
    Assert.That(stream.Codec, Is.EqualTo(CodecTag.None));
  }

  // ------------------------------------------------------------------------------------------
  // Metadata
  // ------------------------------------------------------------------------------------------

  [Test]
  [Category("Unit")]
  public void TheInfoListIsReadIntoTheMetadata() {
    var container = AviTestContainer.Build(
      "MJPG", _WIDTH, _HEIGHT, 24, [_Jpeg(0), _Jpeg(1)],
      info: [("INAM", "A title"), ("IART", "An author"), ("ISFT", "A tool"), ("ICRD", "2001-02-03"), ("ICMT", "A remark")]);

    var metadata = VideoFormatRegistry.ReadMetadata(container);

    Assert.That(metadata.Title, Is.EqualTo("A title"));
    Assert.That(metadata.Artist, Is.EqualTo("An author"));
    Assert.That(metadata.EncodedBy, Is.EqualTo("A tool"));
    Assert.That(metadata.CreationTime!.Value.Date, Is.EqualTo(new DateTime(2001, 2, 3)));
    Assert.That(metadata.TextEntries.Any(t => t.Keyword == "Comment" && t.Text == "A remark"), Is.True);
    Assert.That(metadata.IsEmpty, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheDurationIsWhatTheMainHeaderClaims() {
    // dwMicroSecPerFrame is 100000 and dwTotalFrames is the number of chunks written, so three
    // frames at a tenth of a second each is three tenths.
    var container = AviTestContainer.Build("MJPG", _WIDTH, _HEIGHT, 24, [_Jpeg(0), _Jpeg(1), _Jpeg(2)]);

    Assert.That(VideoFormatRegistry.ReadMetadata(container).Duration, Is.EqualTo(TimeSpan.FromSeconds(0.3)));
  }

  [Test]
  [Category("Unit")]
  public void AStreamCarriesItsNameAndItsLanguage() {
    // 0x0409 is the locale identifier an AVI writes for English (United States). The field is an
    // identifier and not an ISO code, so it is looked up rather than reinterpreted.
    var container = AviTestContainer.Build(
      "MJPG", _WIDTH, _HEIGHT, 24, [_Jpeg(0)], language: 0x0409, streamName: "Main camera");

    var metadata = VideoFormatRegistry.ReadMetadata(container);

    Assert.That(metadata.Streams, Has.Count.EqualTo(1));
    Assert.That(metadata.Streams[0].Kind, Is.EqualTo(MediaStreamKind.Video));
    Assert.That(metadata.Streams[0].Name, Is.EqualTo("Main camera"));
    Assert.That(metadata.Streams[0].Language, Does.StartWith("en"));
    Assert.That(metadata.Streams[0].Codec.ToString(), Is.EqualTo("MJPG"));
  }

  [Test]
  [Category("Unit")]
  public void AFileThatSaysNothingAboutItselfCarriesNoInventedMetadata() {
    var raster = AviTestContainer.BuildBgr24Raster(_WIDTH, _HEIGHT, _RowColours, bottomUp: true);
    var metadata = VideoFormatRegistry.ReadMetadata(AviTestContainer.Build("\0\0\0\0", _WIDTH, _HEIGHT, 24, [raster]));

    Assert.That(metadata.Title, Is.Null);
    Assert.That(metadata.Artist, Is.Null);
    Assert.That(metadata.CreationTime, Is.Null);
    Assert.That(metadata.CoverArt, Is.Empty);
    Assert.That(metadata.Streams[0].Language, Is.Null);
  }

  // ------------------------------------------------------------------------------------------
  // Helpers
  // ------------------------------------------------------------------------------------------

  private static IReadOnlyList<RawImage> _Frames(byte[] container)
    => VideoFormatRegistry.DecodeFrames(container).Select(f => f.Image).ToList();

  private static void _AssertRowColours(RawImage image) {
    Assert.That(image.Width, Is.EqualTo(_WIDTH));
    Assert.That(image.Height, Is.EqualTo(_HEIGHT));

    var rgb = image.ToRgb24();
    for (var row = 0; row < _HEIGHT; ++row) {
      var offset = (row * _WIDTH) * 3;
      Assert.That(rgb[offset], Is.EqualTo(_RowColours[row].R), $"row {row} red");
      Assert.That(rgb[offset + 1], Is.EqualTo(_RowColours[row].G), $"row {row} green");
      Assert.That(rgb[offset + 2], Is.EqualTo(_RowColours[row].B), $"row {row} blue");
    }
  }

  /// <summary>A JPEG whose picture depends on the seed, so that two frames never look alike.</summary>
  internal static byte[] _Jpeg(int seed) {
    var pixels = new byte[_WIDTH * _HEIGHT * 3];
    for (var i = 0; i < _WIDTH * _HEIGHT; ++i) {
      pixels[i * 3] = (byte)((i * 7 + seed * 61) & 0xFF);
      pixels[i * 3 + 1] = (byte)((i * 3 + seed * 29) & 0xFF);
      pixels[i * 3 + 2] = (byte)((i * 11 + seed * 97) & 0xFF);
    }

    var raw = new RawImage { Width = _WIDTH, Height = _HEIGHT, Format = PixelFormat.Rgb24, PixelData = pixels };
    return JpegWriter.ToBytes(JpegFile.FromRawImage(raw));
  }
}
