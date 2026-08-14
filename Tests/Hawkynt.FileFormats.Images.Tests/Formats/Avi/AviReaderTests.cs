using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.Jpeg;

namespace FileFormat.Avi.Tests;

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
  public void FromSpan_NoVideoStream_ThrowsInvalidDataException() {
    var audioOnly = AviTestContainer.Build("MJPG", _WIDTH, _HEIGHT, 24, [_Jpeg(0)], streamType: "auds");

    Assert.Throws<InvalidDataException>(() => AviReader.FromBytes(audioOnly));
  }

  [Test]
  [Category("Unit")]
  public void MotionJpeg_ImageCount_IsTheNumberOfFrameChunks() {
    var file = AviReader.FromBytes(AviTestContainer.Build("MJPG", _WIDTH, _HEIGHT, 24, [_Jpeg(0), _Jpeg(1), _Jpeg(2)]));

    Assert.That(AviFile.ImageCount(file), Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void MotionJpeg_LowercaseFourCC_IsAlsoMotionJpeg() {
    // ffprobe reads a container whose biCompression is 'mjpg' as mjpeg, three frames, exactly as it
    // reads the uppercase one — both spellings are the same codec.
    var file = AviReader.FromBytes(AviTestContainer.Build("mjpg", _WIDTH, _HEIGHT, 24, [_Jpeg(0), _Jpeg(1)]));

    Assert.That(AviFile.ImageCount(file), Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void MotionJpeg_EachFrameEqualsTheSameJpegDecodedOnItsOwn() {
    var jpegs = new[] { _Jpeg(0), _Jpeg(1), _Jpeg(2) };
    var file = AviReader.FromBytes(AviTestContainer.Build("MJPG", _WIDTH, _HEIGHT, 24, jpegs));

    for (var index = 0; index < jpegs.Length; ++index) {
      var throughContainer = AviFile.ToRawImage(file, index);
      var direct = JpegFile.ToRawImage(JpegReader.FromBytes(jpegs[index]));

      Assert.That(throughContainer.Width, Is.EqualTo(direct.Width), $"frame {index} width");
      Assert.That(throughContainer.Height, Is.EqualTo(direct.Height), $"frame {index} height");
      Assert.That(throughContainer.ToRgb24(), Is.EqualTo(direct.ToRgb24()), $"frame {index} pixels");
    }
  }

  [Test]
  [Category("Unit")]
  public void MotionJpeg_FramesAreReturnedInTheOrderTheyWereWritten() {
    // Each frame is a different picture, so a reader handing back the wrong one — or the same one
    // three times — fails here rather than passing on a container whose frames all look alike.
    var jpegs = new[] { _Jpeg(0), _Jpeg(1), _Jpeg(2) };
    var file = AviReader.FromBytes(AviTestContainer.Build("MJPG", _WIDTH, _HEIGHT, 24, jpegs));

    var first = AviFile.ToRawImage(file, 0).ToRgb24();
    var second = AviFile.ToRawImage(file, 1).ToRgb24();
    var third = AviFile.ToRawImage(file, 2).ToRgb24();

    Assert.That(first, Is.Not.EqualTo(second));
    Assert.That(second, Is.Not.EqualTo(third));
  }

  [Test]
  [Category("Unit")]
  public void EmptyFrameChunk_IsNotCountedAsAFrame() {
    // Measured against the oracle: an AVI of four '00dc' chunks one of which is zero-length is
    // reported by `ffprobe -count_frames` as three frames. An empty chunk carries no picture and
    // ffmpeg does not invent one for it, so neither does this.
    var file = AviReader.FromBytes(AviTestContainer.Build("MJPG", _WIDTH, _HEIGHT, 24, [_Jpeg(0), [], _Jpeg(1), _Jpeg(2)]));

    Assert.That(AviFile.ImageCount(file), Is.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void Uncompressed_BottomUpRows_AreFlippedIntoPictureOrder() {
    var raster = AviTestContainer.BuildBgr24Raster(_WIDTH, _HEIGHT, _RowColours, bottomUp: true);
    var file = AviReader.FromBytes(AviTestContainer.Build("\0\0\0\0", _WIDTH, _HEIGHT, 24, [raster]));

    _AssertRowColours(AviFile.ToRawImage(file, 0));
  }

  [Test]
  [Category("Unit")]
  public void Uncompressed_NegativeHeightRunsTopDown() {
    // ffmpeg writes bgr24 rawvideo with biHeight = -37, i.e. top-down, so both signs have to land on
    // the same picture.
    var raster = AviTestContainer.BuildBgr24Raster(_WIDTH, _HEIGHT, _RowColours, bottomUp: false);
    var file = AviReader.FromBytes(AviTestContainer.Build("\0\0\0\0", _WIDTH, -_HEIGHT, 24, [raster]));

    _AssertRowColours(AviFile.ToRawImage(file, 0));
  }

  [Test]
  [Category("Unit")]
  public void Uncompressed_ReportsTheSizeFromTheStreamFormat() {
    var raster = AviTestContainer.BuildBgr24Raster(_WIDTH, _HEIGHT, _RowColours, bottomUp: true);
    var file = AviReader.FromBytes(AviTestContainer.Build("\0\0\0\0", _WIDTH, _HEIGHT, 24, [raster, raster]));

    Assert.That(AviFile.ImageCount(file), Is.EqualTo(2));
    Assert.That(AviFile.ToRawImage(file, 1).Width, Is.EqualTo(_WIDTH));
    Assert.That(AviFile.ToRawImage(file, 1).Height, Is.EqualTo(_HEIGHT));
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
    var file = AviReader.FromBytes(AviTestContainer.Build("\0\0\0\0", _WIDTH, _HEIGHT, 8, [raster], palette));

    _AssertRowColours(AviFile.ToRawImage(file, 0));
  }

  [Test]
  [Category("Unit")]
  public void Uncompressed_ShortFrameChunk_IsRefused() {
    // Half a raster is not a picture. Padding it out would return a frame that is partly invented,
    // which is the one thing a reader must never do quietly.
    var raster = AviTestContainer.BuildBgr24Raster(_WIDTH, _HEIGHT, _RowColours, bottomUp: true);
    var file = AviReader.FromBytes(AviTestContainer.Build("\0\0\0\0", _WIDTH, _HEIGHT, 24, [raster[..(raster.Length / 2)]]));

    Assert.Throws<InvalidDataException>(() => AviFile.ToRawImage(file, 0));
  }

  [TestCase("H264")]
  [TestCase("FMP4")]
  [TestCase("DIVX")]
  [TestCase("XVID")]
  [TestCase("DIB ")]
  [Category("Unit")]
  public void UnsupportedCodec_IsRefusedWithItsFourCharacterCode(string fourCC) {
    var container = AviTestContainer.Build(fourCC, 64, 48, 24, [new byte[64]]);

    var failure = Assert.Throws<NotSupportedException>(() => AviReader.FromBytes(container));
    Assert.That(failure!.Message, Does.Contain(fourCC));
  }

  [Test]
  [Category("Unit")]
  public void Uncompressed_SixteenBitFrames_AreFiveFiveFive() {
    // Refused here while the bitmap reader behind this one read BI_RGB at 16 bits as 5-6-5, which
    // put 395 of 2257 pixels of a gradient wrong against ffmpeg. That reader now takes the layout
    // from the channel masks, so the frame comes back as the file states it and the refusal is gone.
    var raster = AviTestContainer.BuildRgb555Raster(_WIDTH, _HEIGHT, _RowColours, bottomUp: true);
    var file = AviReader.FromBytes(AviTestContainer.Build("\0\0\0\0", _WIDTH, _HEIGHT, 16, [raster]));

    var rgb = AviFile.ToRawImage(file, 0).ToRgb24();
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
    var file = AviReader.FromBytes(AviTestContainer.Build("\0\0\0\0", _WIDTH, _HEIGHT, 32, [raster]));

    var image = AviFile.ToRawImage(file, 0);

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
    var file = AviReader.FromBytes(AviTestContainer.Build("\0\0\0\0", _WIDTH, _HEIGHT, 32, [raster]));

    _AssertRowColours(AviFile.ToRawImage(file, 0));
  }

  [TestCase((short)2)]
  [TestCase((short)48)]
  [Category("Unit")]
  public void Uncompressed_DepthNoBitmapIsStoredAt_IsRefused(short bitsPerPixel) {
    var stride = (_WIDTH * bitsPerPixel / 8 + 3) & ~3;
    var container = AviTestContainer.Build("\0\0\0\0", _WIDTH, _HEIGHT, bitsPerPixel, [new byte[stride * _HEIGHT]]);

    var failure = Assert.Throws<NotSupportedException>(() => AviReader.FromBytes(container));
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
    // of what the file holds.
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
  public void ToRawImage_IndexOutOfRange_Throws() {
    var file = AviReader.FromBytes(AviTestContainer.Build("MJPG", _WIDTH, _HEIGHT, 24, [_Jpeg(0)]));

    Assert.Throws<ArgumentOutOfRangeException>(() => AviFile.ToRawImage(file, 1));
    Assert.Throws<ArgumentOutOfRangeException>(() => AviFile.ToRawImage(file, -1));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImages_ReturnsEveryFrame() {
    var file = AviReader.FromBytes(AviTestContainer.Build("MJPG", _WIDTH, _HEIGHT, 24, [_Jpeg(0), _Jpeg(1)]));

    Assert.That(_AllFrames(file).Count, Is.EqualTo(2));
  }

  /// <summary>Reaches the interface's own all-frames helper, which needs a type parameter to dispatch on.</summary>
  private static IReadOnlyList<RawImage> _AllFrames<T>(T file) where T : IMultiImageFileFormat<T>
    => T.ToRawImages(file);

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
