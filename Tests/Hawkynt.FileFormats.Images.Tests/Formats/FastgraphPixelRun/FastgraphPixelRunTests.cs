using System;
using System.IO;
using System.Linq;
using FileFormat.Core;
using FileFormat.FastgraphPixelRun;

namespace FileFormat.FastgraphPixelRun.Tests;

[TestFixture]
public sealed class FastgraphPixelRunTests {

  private static RawImage _Picture(int width, int height) {
    var pixels = new byte[width * height];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i / 5 % 256);

    return new() {
      Width = width, Height = height, Format = PixelFormat.Indexed8, PixelData = pixels,
      Palette = VgaPalette.Default256, PaletteCount = 256,
    };
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => FastgraphPixelRunReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_WrongMagic_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => FastgraphPixelRunReader.FromBytes(new byte[64]));

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsTheSizeOutOfTheHeader() {
    var file = FastgraphPixelRunReader.FromBytes(FastgraphPixelRunWriter.ToBytes(FastgraphPixelRunFile.FromRawImage(_Picture(40, 25))));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(40));
      Assert.That(file.Height, Is.EqualTo(25));
      Assert.That(file.Pixels, Has.Length.EqualTo(40 * 25));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesEveryHeaderByteAsAWordWithAZeroHalf() {
    var data = FastgraphPixelRunWriter.ToBytes(FastgraphPixelRunFile.FromRawImage(_Picture(320, 200)));

    Assert.Multiple(() => {
      Assert.That(data[16], Is.EqualTo(319 & 0xFF), "the low half of the largest x");
      Assert.That(data[18], Is.EqualTo(319 >> 8), "the high half of it");
      Assert.That(data[20], Is.EqualTo(199), "the largest y");
      for (var at = 17; at < FastgraphPixelRunFile.HeaderSize; at += 2)
        Assert.That(data[at], Is.Zero, $"byte {at} pads the word before it");
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ARunStreamThatDoesNotCoverTheStatedSize_ThrowsInvalidDataException() {
    var data = FastgraphPixelRunWriter.ToBytes(FastgraphPixelRunFile.FromRawImage(_Picture(32, 8)));
    Array.Resize(ref data, data.Length - 2);

    Assert.Throws<InvalidDataException>(() => FastgraphPixelRunReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ARunStreamThatOverrunsTheStatedSize_ThrowsInvalidDataException() {
    var data = FastgraphPixelRunWriter.ToBytes(FastgraphPixelRunFile.FromRawImage(_Picture(32, 8)));
    var longer = new byte[data.Length + 2];
    data.CopyTo(longer, 0);
    longer[^2] = 3;
    longer[^1] = 200;

    Assert.Throws<InvalidDataException>(() => FastgraphPixelRunReader.FromBytes(longer));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AHeaderWordWithANonZeroHalf_ThrowsInvalidDataException() {
    var data = FastgraphPixelRunWriter.ToBytes(FastgraphPixelRunFile.FromRawImage(_Picture(16, 4)));
    data[17] = 1;

    Assert.Throws<InvalidDataException>(() => FastgraphPixelRunReader.FromBytes(data));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TheIndicesComeBackByteForByte() {
    var source = _Picture(64, 40);
    var decoded = FastgraphPixelRunFile.ToRawImage(
      FastgraphPixelRunReader.FromBytes(FastgraphPixelRunWriter.ToBytes(FastgraphPixelRunFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(64));
      Assert.That(decoded.Height, Is.EqualTo(40));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(decoded.PaletteCount, Is.EqualTo(256));
      Assert.That(decoded.PixelData.SequenceEqual(source.PixelData), Is.True);
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ARunLongerThanACountByte() {
    var flat = new RawImage {
      Width = 320, Height = 4, Format = PixelFormat.Indexed8, PixelData = new byte[320 * 4],
      Palette = VgaPalette.Default256, PaletteCount = 256,
    };

    var decoded = FastgraphPixelRunFile.ToRawImage(
      FastgraphPixelRunReader.FromBytes(FastgraphPixelRunWriter.ToBytes(FastgraphPixelRunFile.FromRawImage(flat))));

    Assert.That(decoded.PixelData.SequenceEqual(flat.PixelData), Is.True, "1280 pixels of one colour split into runs and come back whole");
  }
}
