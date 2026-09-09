using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.HighresMedium.Tests;

[TestFixture]
public sealed class HighresMediumReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HighresMediumReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HighresMediumReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(
      () => HighresMediumReader.FromFile(new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hrm"))));

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => HighresMediumReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void TheOldDoubledScreenLength_IsNoLongerWhatItWants()
    => Assert.Throws<InvalidDataException>(() => HighresMediumReader.FromBytes(new byte[64064]));

  [Test]
  [Category("Unit")]
  public void FromBytes_SplitsTheScreenFromTheRowPalettes() {
    var data = _BuildMinimalFile();
    var result = HighresMediumReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(result.BitmapData, Has.Length.EqualTo(64000));
      Assert.That(result.Palettes, Has.Length.EqualTo(28000));
      Assert.That(result.BitmapData[0], Is.EqualTo(data[0]));
      Assert.That(result.Palettes[0], Is.EqualTo(data[HighresMediumFile.PalettesOffset]));
    });
  }

  /// <summary>A row's own palette is what colours it, not one the whole picture shares.</summary>
  [Test]
  [Category("Unit")]
  public void EachRowTakesItsColoursFromItsOwnPalette() {
    var data = _BuildMinimalFile();

    // Plane value 1 at the left of the line asks for entry 0; give two rows different ones.
    _PutEntry(data, 0, 0, 0x0700);
    _PutEntry(data, 2, 0, 0x0007);

    var picture = HighresMediumFile.ToRawImage(HighresMediumReader.FromBytes(data));

    Assert.Multiple(() => {
      Assert.That(picture.PixelData[0], Is.Not.Zero, "row 0 is red");
      Assert.That(picture.PixelData[2 * 640 * 3 + 2], Is.Not.Zero, "row 2 is blue");
      Assert.That(picture.PixelData[2 * 640 * 3], Is.Zero, "and has no red in it");
    });
  }

  [Test]
  [Category("Unit")]
  public void RowsAreShownInPairs() {
    var data = _BuildMinimalFile();
    _PutEntry(data, 0, 0, 0x0700);
    _PutEntry(data, 1, 0, 0x0000);

    var picture = HighresMediumFile.ToRawImage(HighresMediumReader.FromBytes(data));

    // A pair is one row of picture, so both rows show the average of the two.
    Assert.That(picture.PixelData[0], Is.EqualTo(picture.PixelData[640 * 3]));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidParsesCorrectly() {
    using var ms = new MemoryStream(_BuildMinimalFile());
    var result = HighresMediumReader.FromStream(ms);

    Assert.That(result.BitmapData, Has.Length.EqualTo(64000));
  }

  private static void _PutEntry(byte[] data, int row, int entry, int word) {
    var at = HighresMediumFile.PalettesOffset + row * HighresMediumFile.PaletteRowSize + entry * 2;
    data[at] = (byte)(word >> 8);
    data[at + 1] = (byte)word;
  }

  /// <summary>A screen whose every pixel is plane value 1, so entry 0 of each row is what shows.</summary>
  private static byte[] _BuildMinimalFile() {
    var data = new byte[HighresMediumFile.FileSize];
    for (var y = 0; y < HighresMediumFile.ImageHeight; ++y)
    for (var word = 0; word < 40; ++word) {
      data[y * HighresMediumFile.BytesPerRow + word * 4] = 0xFF;
      data[y * HighresMediumFile.BytesPerRow + word * 4 + 1] = 0xFF;
    }

    return data;
  }
}
