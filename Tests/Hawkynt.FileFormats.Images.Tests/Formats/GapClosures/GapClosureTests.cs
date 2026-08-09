using System;
using System.IO;
using System.Linq;
using FileFormat.AirNav;
using FileFormat.ApolloHdru;
using FileFormat.Arf;
using FileFormat.Core;
using FileFormat.PlaybackBitmapSequence;
using FileFormat.TmSat;

namespace Hawkynt.FileFormats.Images.Tests.GapClosures;

/// <summary>
/// The five small formats closed from XnView's own behaviour: a playback bitmap sequence, an AirNav
/// picture, an ARF, an Apollo HDRU page and a TMSat frame. Each fixture is built to the layout
/// XnView's converter was shown to expect, and each refusal is one of the cases that showed it.
/// </summary>
[TestFixture]
public sealed class GapClosureTests {

  private static byte[] _Bmp8(int width, int height) {
    var stride = (width + 3) & ~3;
    var output = new byte[54 + 1024 + stride * height];
    output[0] = (byte)'B';
    output[1] = (byte)'M';
    _Write(output, 2, output.Length);
    _Write(output, 10, 54 + 1024);
    _Write(output, 14, 40);
    _Write(output, 18, width);
    _Write(output, 22, height);
    output[26] = 1;
    output[28] = 8;
    _Write(output, 46, 256);
    for (var i = 0; i < 256; ++i) {
      output[54 + i * 4] = (byte)(255 - i);
      output[54 + i * 4 + 1] = (byte)(i / 2);
      output[54 + i * 4 + 2] = (byte)i;
    }

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        output[54 + 1024 + y * stride + x] = (byte)((x + y * 13) % 256);

    return output;
  }

  private static void _Write(byte[] data, int at, int value) {
    data[at] = (byte)value;
    data[at + 1] = (byte)(value >> 8);
    data[at + 2] = (byte)(value >> 16);
    data[at + 3] = (byte)(value >> 24);
  }

  // -------- Playback Bitmap Sequence --------

  [Test]
  [Category("Unit")]
  public void PlaybackBitmapSequence_ReadsTheBitmapBehindItsHeader() {
    var data = PlaybackBitmapSequenceFile.Magic.ToArray().Concat(new byte[6]).Concat(_Bmp8(9, 6)).ToArray();
    var image = PlaybackBitmapSequenceFile.ToRawImage(PlaybackBitmapSequenceReader.FromBytes(data));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(9));
      Assert.That(image.Height, Is.EqualTo(6));
    });
  }

  [Test]
  [Category("Unit")]
  public void PlaybackBitmapSequence_WithoutTheTenLettersIsRefused() {
    var data = "BMSWinPlaX"u8.ToArray().Concat(new byte[6]).Concat(_Bmp8(4, 4)).ToArray();
    Assert.Throws<InvalidDataException>(() => PlaybackBitmapSequenceReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void PlaybackBitmapSequence_WithNoBitmapBehindTheHeaderIsRefused()
    => Assert.Throws<InvalidDataException>(() => PlaybackBitmapSequenceReader.FromBytes(PlaybackBitmapSequenceFile.Magic.ToArray().Concat(new byte[64]).ToArray()));

  // -------- AirNav --------

  [Test]
  [Category("Unit")]
  public void AirNav_ReadsTheFixedOffsetsThePictureUses() {
    var data = _Bmp8(11, 7);
    data[0] = (byte)'A';
    data[1] = (byte)'N';

    var file = AirNavReader.FromBytes(data);
    var image = AirNavFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(11));
      Assert.That(image.Height, Is.EqualTo(7));
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Indexed8));

      // Rows are stored from the bottom up, so the first row read is the last row written.
      Assert.That(image.PixelData[0], Is.EqualTo((byte)((6 * 13) % 256)));
      Assert.That(image.Palette![3], Is.EqualTo((byte)1), "the table is blue first in the file and red first here");
    });
  }

  [Test]
  [Category("Unit")]
  public void AirNav_AWindowsBitmapUnderItsOwnNameIsRefused()
    => Assert.Throws<InvalidDataException>(() => AirNavReader.FromBytes(_Bmp8(4, 4)));

  [Test]
  [Category("Unit")]
  public void AirNav_APictureThatIsNotEightBitsIsRefused() {
    var data = _Bmp8(4, 4);
    data[0] = (byte)'A';
    data[1] = (byte)'N';
    data[28] = 24;
    Assert.Throws<InvalidDataException>(() => AirNavReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void AirNav_RoundTrips() {
    var source = new RawImage {
      Width = 5,
      Height = 3,
      Format = PixelFormat.Indexed8,
      PixelData = Enumerable.Range(0, 15).Select(i => (byte)(i * 7)).ToArray(),
      Palette = Enumerable.Range(0, 768).Select(i => (byte)i).ToArray(),
      PaletteCount = 256,
    };

    var read = AirNavReader.FromBytes(AirNavWriter.ToBytes(AirNavFile.FromRawImage(source)));
    Assert.That(read.PixelData, Is.EqualTo(source.PixelData));
  }

  // -------- ARF --------

  private static byte[] _Arf(int width, int height, int version = 2, int type = 0, int offset = ArfFile.HeaderSize, int pixels = -1) {
    var count = pixels < 0 ? width * height : pixels;
    var output = new byte[offset + count];
    ArfFile.Magic.CopyTo(output);
    _WriteBig(output, 4, version);
    _WriteBig(output, 8, height);
    _WriteBig(output, 12, width);
    _WriteBig(output, 16, type);
    _WriteBig(output, 24, offset);
    for (var i = 0; i < count; ++i)
      output[offset + i] = (byte)(i * 7 % 251);

    return output;
  }

  private static void _WriteBig(byte[] data, int at, int value) {
    data[at] = (byte)(value >> 24);
    data[at + 1] = (byte)(value >> 16);
    data[at + 2] = (byte)(value >> 8);
    data[at + 3] = (byte)value;
  }

  [Test]
  [Category("Unit")]
  public void Arf_ReadsTheSizeAndThePictureAtTheStatedOffset() {
    var file = ArfReader.FromBytes(_Arf(7, 5, offset: 64));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(7));
      Assert.That(file.Height, Is.EqualTo(5));
      Assert.That(ArfFile.ToRawImage(file).Format, Is.EqualTo(PixelFormat.Gray8));
      Assert.That(file.PixelData[0], Is.EqualTo((byte)0));
    });
  }

  /// <summary>Axon's Raw Format is a different thing under the same name, and this reader is not it.</summary>
  [Test]
  [Category("Unit")]
  public void Arf_AnAxonRawFormatFileIsRefused() {
    byte[] axon = [0x01, 0x00, (byte)'A', (byte)'R', 0x01, 0x00, 0x04, 0x00, 0x02, 0x00, 0x08, 0x00, 0x01, 0x00];
    Assert.Throws<InvalidDataException>(() => ArfReader.FromBytes(axon.Concat(new byte[600]).ToArray()));
  }

  [Test]
  [Category("Unit")]
  public void Arf_AVersionOtherThanTwoIsRefused()
    => Assert.Throws<InvalidDataException>(() => ArfReader.FromBytes(_Arf(4, 4, version: 1)));

  [Test]
  [Category("Unit")]
  public void Arf_AnImageTypeAboveTwoIsRefused()
    => Assert.Throws<InvalidDataException>(() => ArfReader.FromBytes(_Arf(4, 4, type: 3)));

  [Test]
  [Category("Unit")]
  public void Arf_APictureShorterThanItsOwnSizeIsRefused()
    => Assert.Throws<InvalidDataException>(() => ArfReader.FromBytes(_Arf(4, 4, pixels: 8)));

  [Test]
  [Category("Unit")]
  public void Arf_RoundTrips() {
    var pixels = Enumerable.Range(0, 20).Select(i => (byte)(i * 3)).ToArray();
    var read = ArfReader.FromBytes(ArfWriter.ToBytes(new() { Width = 5, Height = 4, PixelData = pixels }));
    Assert.That(read.PixelData, Is.EqualTo(pixels));
  }

  // -------- Apollo HDRU --------

  private static byte[] _Hdru(int width, int height, int compression = 0, int resolution = 300, int shortBy = 0) {
    var stride = (width + 7) / 8;
    var output = new byte[ApolloHdruFile.HeaderSize + stride * height - shortBy];
    ApolloHdruFile.Magic.CopyTo(output);
    output[2] = (byte)(compression >> 8);
    output[3] = (byte)compression;
    output[4] = (byte)(resolution >> 8);
    output[5] = (byte)resolution;
    output[6] = (byte)(width >> 8);
    output[7] = (byte)width;
    output[8] = (byte)(height >> 8);
    output[9] = (byte)height;
    for (var i = ApolloHdruFile.HeaderSize; i < output.Length; ++i)
      output[i] = (byte)(i * 29 % 251);

    return output;
  }

  [Test]
  [Category("Unit")]
  public void ApolloHdru_ReadsThePageTheHeaderStates() {
    var file = ApolloHdruReader.FromBytes(_Hdru(64, 32));
    var image = ApolloHdruFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(64));
      Assert.That(image.Height, Is.EqualTo(32));
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Indexed1));
      Assert.That(file.Resolution, Is.EqualTo(300));

      // A set bit is white, so the second colour is the light one.
      Assert.That(image.Palette![0], Is.EqualTo((byte)0));
      Assert.That(image.Palette[3], Is.EqualTo((byte)0xFF));
    });
  }

  [Test]
  [Category("Unit")]
  public void ApolloHdru_WithoutTheTwoBytesItOpensWithIsRefused() {
    var data = _Hdru(32, 8);
    data[1] = 0x02;
    Assert.Throws<InvalidDataException>(() => ApolloHdruReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void ApolloHdru_ACompressedPageIsRefusedRatherThanGuessedAt()
    => Assert.Throws<InvalidDataException>(() => ApolloHdruReader.FromBytes(_Hdru(32, 8, compression: 2)));

  [Test]
  [Category("Unit")]
  public void ApolloHdru_APageShorterThanItsOwnSizeIsRefused()
    => Assert.Throws<InvalidDataException>(() => ApolloHdruReader.FromBytes(_Hdru(32, 8, shortBy: 4)));

  [Test]
  [Category("Unit")]
  public void ApolloHdru_RoundTrips() {
    var rows = Enumerable.Range(0, 4 * 8).Select(i => (byte)(i * 13)).ToArray();
    var read = ApolloHdruReader.FromBytes(ApolloHdruWriter.ToBytes(new() { Width = 32, Height = 8, Resolution = 200, PixelData = rows }));

    Assert.Multiple(() => {
      Assert.That(read.Resolution, Is.EqualTo(200));
      Assert.That(read.PixelData, Is.EqualTo(rows));
    });
  }

  // -------- TMSat --------

  [Test]
  [Category("Unit")]
  public void TmSat_ReadsTheOneLengthTheFormatHas() {
    var data = new byte[TmSatFile.FileSize];
    for (var i = 0; i < data.Length; i += 997)
      data[i] = (byte)(i % 256);

    var image = TmSatFile.ToRawImage(TmSatReader.FromBytes(data));
    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(TmSatFile.Side));
      Assert.That(image.Height, Is.EqualTo(TmSatFile.Side));
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Gray8));
      Assert.That(image.PixelData, Is.EqualTo(data));
    });
  }

  /// <summary>
  /// The wide-angle camera's frames are 352,192 bytes and are not this. With no header to read, one
  /// byte either way is the whole of the difference between a picture and a file of some other kind.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void TmSat_AnyOtherLengthIsRefused() {
    Assert.Throws<InvalidDataException>(() => TmSatReader.FromBytes(new byte[352192]));
    Assert.Throws<InvalidDataException>(() => TmSatReader.FromBytes(new byte[TmSatFile.FileSize - 1]));
    Assert.Throws<InvalidDataException>(() => TmSatReader.FromBytes(new byte[TmSatFile.FileSize + 1]));
  }
}
