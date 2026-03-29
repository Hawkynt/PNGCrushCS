using System;
using FileFormat.AmigaIcon;

namespace FileFormat.AmigaIcon.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void AmigaIconFile_DefaultPlanarData_IsEmptyArray() {
    var file = new AmigaIconFile();
    Assert.That(file.PlanarData, Is.Not.Null);
    Assert.That(file.PlanarData.Length, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void AmigaIconFile_DefaultIconType_IsTool() {
    var file = new AmigaIconFile();
    Assert.That(file.IconType, Is.EqualTo((int)AmigaIconType.Tool));
  }

  [Test]
  [Category("Unit")]
  public void AmigaIconFile_DefaultPalette_IsNull() {
    var file = new AmigaIconFile();
    Assert.That(file.Palette, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void AmigaIconFile_DefaultRawHeader_IsNull() {
    var file = new AmigaIconFile();
    Assert.That(file.RawHeader, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void AmigaIconFile_InitProperties_RoundTrip() {
    var planarData = new byte[] { 0xFF, 0x00 };
    var palette = new byte[] { 1, 2, 3, 4, 5, 6 };
    var file = new AmigaIconFile {
      Width = 16,
      Height = 8,
      Depth = 2,
      IconType = (int)AmigaIconType.Drawer,
      PlanarData = planarData,
      Palette = palette,
    };

    Assert.That(file.Width, Is.EqualTo(16));
    Assert.That(file.Height, Is.EqualTo(8));
    Assert.That(file.Depth, Is.EqualTo(2));
    Assert.That(file.IconType, Is.EqualTo((int)AmigaIconType.Drawer));
    Assert.That(file.PlanarData, Is.SameAs(planarData));
    Assert.That(file.Palette, Is.SameAs(palette));
  }

  [Test]
  [Category("Unit")]
  public void AmigaIconType_HasExpectedValues() {
    Assert.That((int)AmigaIconType.Disk, Is.EqualTo(1));
    Assert.That((int)AmigaIconType.Drawer, Is.EqualTo(2));
    Assert.That((int)AmigaIconType.Tool, Is.EqualTo(3));
    Assert.That((int)AmigaIconType.Project, Is.EqualTo(4));
    Assert.That((int)AmigaIconType.Garbage, Is.EqualTo(5));
    Assert.That((int)AmigaIconType.Device, Is.EqualTo(6));
    Assert.That((int)AmigaIconType.Kick, Is.EqualTo(7));
    Assert.That((int)AmigaIconType.AppIcon, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void AmigaIconType_HasExpectedCount() {
    var values = Enum.GetValues<AmigaIconType>();
    Assert.That(values.Length, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void DefaultPalette_Has4Colors() {
    Assert.That(AmigaIconFile.DefaultPalette.Length, Is.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void DefaultPalette_FirstColorIsGray() {
    Assert.That(AmigaIconFile.DefaultPalette[0], Is.EqualTo(0x95));
    Assert.That(AmigaIconFile.DefaultPalette[1], Is.EqualTo(0x95));
    Assert.That(AmigaIconFile.DefaultPalette[2], Is.EqualTo(0x95));
  }

  [Test]
  [Category("Unit")]
  public void DefaultPalette_SecondColorIsBlack() {
    Assert.That(AmigaIconFile.DefaultPalette[3], Is.EqualTo(0x00));
    Assert.That(AmigaIconFile.DefaultPalette[4], Is.EqualTo(0x00));
    Assert.That(AmigaIconFile.DefaultPalette[5], Is.EqualTo(0x00));
  }

  [Test]
  [Category("Unit")]
  public void BytesPerPlaneRow_WordAligned() {
    Assert.That(AmigaIconFile.BytesPerPlaneRow(1), Is.EqualTo(2));
    Assert.That(AmigaIconFile.BytesPerPlaneRow(8), Is.EqualTo(2));
    Assert.That(AmigaIconFile.BytesPerPlaneRow(16), Is.EqualTo(2));
    Assert.That(AmigaIconFile.BytesPerPlaneRow(17), Is.EqualTo(4));
    Assert.That(AmigaIconFile.BytesPerPlaneRow(32), Is.EqualTo(4));
    Assert.That(AmigaIconFile.BytesPerPlaneRow(33), Is.EqualTo(6));
  }

  [Test]
  [Category("Unit")]
  public void PlanarDataSize_MatchesExpectation() {
    // 32 wide, 16 tall, 2 planes: bytesPerPlaneRow=4, total=4*16*2=128
    Assert.That(AmigaIconFile.PlanarDataSize(32, 16, 2), Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void AmigaIconHeader_StructSize_Is78() {
    Assert.That(AmigaIconHeader.StructSize, Is.EqualTo(78));
  }

  [Test]
  [Category("Unit")]
  public void AmigaIconHeader_MagicValue() {
    Assert.That(AmigaIconHeader.MagicValue, Is.EqualTo(0xE310));
  }
}
