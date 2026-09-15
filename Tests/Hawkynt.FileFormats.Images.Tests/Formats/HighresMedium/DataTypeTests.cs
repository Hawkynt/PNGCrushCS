namespace FileFormat.HighresMedium.Tests;

/// <summary>
/// What a HighresMedium picture is, as opposed to what it used to be modelled as.
/// </summary>
/// <remarks>
/// The old numbers described two whole 640 by 200 screens with a sixteen-word plain ST palette
/// apiece, 64064 bytes. The format is 64000 bytes of screen at 640 by 400 and then a palette of 35
/// STE colours for every one of those 400 rows.
/// </remarks>
[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void FileSize_Is92000()
    => Assert.That(HighresMediumFile.FileSize, Is.EqualTo(92000));

  [Test]
  [Category("Unit")]
  public void ImageWidth_Is640()
    => Assert.That(HighresMediumFile.ImageWidth, Is.EqualTo(640));

  [Test]
  [Category("Unit")]
  public void ImageHeight_Is400()
    => Assert.That(HighresMediumFile.ImageHeight, Is.EqualTo(400));

  [Test]
  [Category("Unit")]
  public void NumPlanes_Is2()
    => Assert.That(HighresMediumFile.NumPlanes, Is.EqualTo(2));

  [Test]
  [Category("Unit")]
  public void ColorCount_Is4()
    => Assert.That(HighresMediumFile.ColorCount, Is.EqualTo(4));

  [Test]
  [Category("Unit")]
  public void ARowCarriesThirtyFiveColours() {
    Assert.Multiple(() => {
      Assert.That(HighresMediumFile.PaletteEntryCount, Is.EqualTo(35));
      Assert.That(HighresMediumFile.PalettesOffset, Is.EqualTo(64000));
    });
  }

  /// <summary>
  /// Each register changes over at its own column, which is what the four leads are for.
  /// </summary>
  /// <remarks>
  /// Register 3 is loaded 32 pixels before the split and register 0 a whole 80, so at the very left
  /// of the line they are already two zones apart in the table. Working the zone out from the pixel
  /// alone would give every value the same entry and put a stripe of the wrong colour down the left
  /// of every split.
  /// </remarks>
  [Test]
  [Category("Unit")]
  public void EachRegisterChangesOverAtItsOwnColumn() {
    Assert.Multiple(() => {
      Assert.That(HighresMediumFile.EntryFor(0, 0), Is.EqualTo(3));
      Assert.That(HighresMediumFile.EntryFor(0, 1), Is.EqualTo(0));
      Assert.That(HighresMediumFile.EntryFor(0, 2), Is.EqualTo(1));
      Assert.That(HighresMediumFile.EntryFor(0, 3), Is.EqualTo(2));
      Assert.That(HighresMediumFile.EntryFor(639, 3), Is.EqualTo(34));
    });
  }
}
