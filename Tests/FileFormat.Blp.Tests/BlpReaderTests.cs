using System;
using System.IO;
using FileFormat.Blp;

namespace FileFormat.Blp.Tests;

[TestFixture]
public sealed class BlpReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BlpReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BlpReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".blp"));
    Assert.Throws<FileNotFoundException>(() => BlpReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BlpReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    var tooSmall = new byte[10];
    Assert.Throws<InvalidDataException>(() => BlpReader.FromBytes(tooSmall));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_InvalidMagic_ThrowsInvalidDataException() {
    var bad = new byte[148];
    bad[0] = (byte)'X';
    bad[1] = (byte)'Y';
    bad[2] = (byte)'Z';
    bad[3] = (byte)'W';
    Assert.Throws<InvalidDataException>(() => BlpReader.FromBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidPaletteBLP_ParsesCorrectly() {
    var blp = BlpTestHelper.BuildPaletteBlp(8, 8, alphaDepth: 0);
    var result = BlpReader.FromBytes(blp);

    Assert.That(result.Width, Is.EqualTo(8));
    Assert.That(result.Height, Is.EqualTo(8));
    Assert.That(result.Encoding, Is.EqualTo(BlpEncoding.Palette));
    Assert.That(result.Palette, Is.Not.Null);
    Assert.That(result.Palette!.Length, Is.EqualTo(1024));
    Assert.That(result.MipData, Has.Length.EqualTo(1));
    Assert.That(result.HasMips, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidBgraBLP_ParsesCorrectly() {
    var blp = BlpTestHelper.BuildBgraBlp(4, 4);
    var result = BlpReader.FromBytes(blp);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.Encoding, Is.EqualTo(BlpEncoding.UncompressedBgra));
    Assert.That(result.Palette, Is.Null);
    Assert.That(result.MipData, Has.Length.EqualTo(1));
    Assert.That(result.MipData[0].Length, Is.EqualTo(4 * 4 * 4));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PaletteWithAlpha8_ParsesAlphaDepth() {
    var blp = BlpTestHelper.BuildPaletteBlp(4, 4, alphaDepth: 8);
    var result = BlpReader.FromBytes(blp);

    Assert.That(result.AlphaDepth, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidBgra_ParsesCorrectly() {
    var blp = BlpTestHelper.BuildBgraBlp(4, 4);
    using var stream = new MemoryStream(blp);
    var result = BlpReader.FromStream(stream);

    Assert.That(result.Width, Is.EqualTo(4));
    Assert.That(result.Height, Is.EqualTo(4));
    Assert.That(result.Encoding, Is.EqualTo(BlpEncoding.UncompressedBgra));
  }
}
