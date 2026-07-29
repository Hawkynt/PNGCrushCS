using System;
using System.IO;
using FileFormat.AtariTxs;
using FileFormat.Core;

namespace FileFormat.AtariTxs.Tests;

[TestFixture]
public sealed class AtariTxsTests {

  private static byte[] _File(Func<int, byte> value) {
    var data = new byte[AtariTxsFile.FileSize];
    AtariTxsFile.Header.CopyTo(data);
    for (var i = 0; i < 256; ++i)
      data[AtariTxsFile.Header.Length + i] = value(i);

    return data;
  }

  [Test]
  public void Reader_RejectsAWrongHeader() {
    var data = _File(_ => 0);
    data[0] = 0;

    Assert.Throws<InvalidDataException>(() => AtariTxsReader.FromBytes(data));
  }

  [Test]
  public void Reader_RejectsAValueThatIsNotAColor() {
    Assert.Throws<InvalidDataException>(() => AtariTxsReader.FromBytes(_File(i => (byte)(i == 5 ? 16 : 0))));
  }

  [Test]
  public void Reader_RejectsAnyOtherLength() {
    Assert.Throws<InvalidDataException>(() => AtariTxsReader.FromBytes(new byte[AtariTxsFile.FileSize + 1]));
  }

  [Test]
  public void EachStoredValue_CoversAFourByFourBlock() {
    var image = AtariTxsFile.ToRawImage(AtariTxsReader.FromBytes(_File(i => (byte)(i == 0 ? 15 : 0))));

    Assert.Multiple(() => {
      for (var y = 0; y < AtariTxsFile.Scale; ++y)
      for (var x = 0; x < AtariTxsFile.Scale; ++x)
        Assert.That(image.PixelData[y * AtariTxsFile.DisplaySize + x], Is.EqualTo(15), $"{x},{y}");

      Assert.That(image.PixelData[AtariTxsFile.Scale], Is.Zero, "the next block is untouched");
      Assert.That(image.PixelData[AtariTxsFile.Scale * AtariTxsFile.DisplaySize], Is.Zero);
    });
  }

  [Test]
  public void Dimensions_AreTheScaledOnes() {
    var image = AtariTxsFile.ToRawImage(AtariTxsReader.FromBytes(_File(_ => 0)));

    Assert.That((image.Width, image.Height), Is.EqualTo((64, 64)));
  }

  [Test]
  public void Palette_IsTheSixteenGreysOfHueZero() {
    var image = AtariTxsFile.ToRawImage(AtariTxsReader.FromBytes(_File(_ => 0)));

    Assert.Multiple(() => {
      Assert.That(image.PaletteCount, Is.EqualTo(16));
      Assert.That(image.Palette, Is.EqualTo(Atari8BitGraphics.Palette[..48].ToArray()));
      // Hue 0 is grey all the way up, which is what makes this a texture rather than a picture.
      for (var i = 0; i < 16; ++i)
        Assert.That(image.Palette![i * 3 + 1], Is.EqualTo(image.Palette[i * 3]), $"entry {i} is not grey");
    });
  }

  [Test]
  public void RoundTrip_PreservesEveryValue() {
    var data = _File(i => (byte)(i & 15));
    var reread = AtariTxsReader.FromBytes(AtariTxsWriter.ToBytes(AtariTxsReader.FromBytes(data)));

    Assert.That(reread.Values, Is.EqualTo(data[AtariTxsFile.Header.Length..]));
  }

  [Test]
  public void EncodingAGreyStep_RecoversTheSameValues() {
    var source = AtariTxsFile.ToRawImage(AtariTxsReader.FromBytes(_File(i => (byte)(i & 15))));
    var again = AtariTxsFile.FromRawImage(PixelConverter.Convert(source, PixelFormat.Rgb24));

    Assert.That(again.Values, Is.EqualTo(AtariTxsReader.FromBytes(_File(i => (byte)(i & 15))).Values));
  }
}
