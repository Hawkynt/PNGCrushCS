using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.SuperHiresEditor.Tests;

/// <summary>
/// What a Super Hires Editor logo is, as opposed to what it used to be modelled as.
/// </summary>
/// <remarks>
/// The old model was two whole 320 by 200 high-resolution screens, 18002 bytes, with nothing that
/// was sprite. The format is 3250: a 96 by 88 logo — twelve cells across and eleven down — with
/// eight sprites over the top 84 rows in two layers of four, one colour to a layer.
/// </remarks>
[TestFixture]
public sealed class SuperHiresEditorReaderTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SuperHiresEditorReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SuperHiresEditorReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException()
    => Assert.Throws<FileNotFoundException>(
      () => SuperHiresEditorReader.FromFile(new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".she"))));

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => SuperHiresEditorReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void AnythingShorterThanAWholeLogoIsRefused()
    => Assert.Throws<InvalidDataException>(() => SuperHiresEditorReader.FromBytes(new byte[3249]));

  [Test]
  [Category("Unit")]
  public void ThePartsAreWhereTheMachineAddressesThem() {
    Assert.Multiple(() => {
      Assert.That(SuperHiresEditorFile.FileSize, Is.EqualTo(3250));
      Assert.That(SuperHiresEditorFile.BitmapOffset, Is.EqualTo(2));
      Assert.That(SuperHiresEditorFile.ScreenOffset, Is.EqualTo(1058));
      Assert.That(SuperHiresEditorFile.BackSpritesOffset, Is.EqualTo(1190));
      Assert.That(SuperHiresEditorFile.FrontSpritesOffset, Is.EqualTo(1446));
      Assert.That(SuperHiresEditorFile.BackColorOffset, Is.EqualTo(3238));
      Assert.That(SuperHiresEditorFile.FrontColorOffset, Is.EqualTo(3239));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheLogoIsNinetySixByEightyEight() {
    var picture = SuperHiresEditorFile.ToRawImage(SuperHiresEditorReader.FromBytes(_BuildValidFile(0x2000)));

    Assert.Multiple(() => {
      Assert.That(picture.Width, Is.EqualTo(96));
      Assert.That(picture.Height, Is.EqualTo(88));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidFile_ParsesLoadAddress()
    => Assert.That(SuperHiresEditorReader.FromBytes(_BuildValidFile(0x2000)).LoadAddress, Is.EqualTo(0x2000));

  [Test]
  [Category("Unit")]
  public void TheBitmapDecidesWhereNoSpriteCovers() {
    var data = _BuildValidFile(0x2000);

    // Cell 0 of the last character row, which is below the sprites: white on black, all lit.
    var cell = 10 * SuperHiresEditorFile.Columns;
    data[SuperHiresEditorFile.ScreenOffset + cell] = 0x10;
    for (var line = 0; line < 8; ++line)
      data[SuperHiresEditorFile.BitmapOffset + cell * 8 + line] = 0xFF;

    var picture = SuperHiresEditorFile.ToRawImage(SuperHiresEditorReader.FromBytes(data));

    Assert.That(picture.PixelData[80 * 96], Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ASpriteOverThePixelWinsOverTheBitmap() {
    var data = _BuildValidFile(0x2000);
    data[SuperHiresEditorFile.BackColorOffset] = 0x07;
    data[SuperHiresEditorFile.FrontColorOffset] = 0x0A;

    // The top-left pixel of the back layer's first sprite, and then of the front layer's.
    data[SuperHiresEditorFile.BackSpritesOffset] = 0x80;
    var back = SuperHiresEditorFile.ToRawImage(SuperHiresEditorReader.FromBytes(data));

    data[SuperHiresEditorFile.FrontSpritesOffset] = 0x80;
    var front = SuperHiresEditorFile.ToRawImage(SuperHiresEditorReader.FromBytes(data));

    Assert.Multiple(() => {
      Assert.That(back.PixelData[0], Is.EqualTo(0x07));
      Assert.That(front.PixelData[0], Is.EqualTo(0x0A), "and the front layer wins over the back");
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidFile_ParsesCorrectly() {
    using var ms = new MemoryStream(_BuildValidFile(0x4000));
    var result = SuperHiresEditorReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
      Assert.That(result.BitmapData, Has.Length.EqualTo(1056));
      Assert.That(result.ScreenData, Has.Length.EqualTo(132));
      Assert.That(result.Sprites, Has.Length.EqualTo(2048));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var original = SuperHiresEditorReader.FromBytes(_BuildValidFile(0x2000));
    var bytes = SuperHiresEditorWriter.ToBytes(original);
    var restored = SuperHiresEditorReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(SuperHiresEditorFile.FileSize));
      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
      Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
      Assert.That(restored.Sprites, Is.EqualTo(original.Sprites));
      Assert.That(restored.BackSpriteColor, Is.EqualTo(original.BackSpriteColor));
      Assert.That(restored.FrontSpriteColor, Is.EqualTo(original.FrontSpriteColor));
      Assert.That(restored.Trailer, Is.EqualTo(original.Trailer));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_EveryByteSaturated() {
    // Every field at its widest, which is what the old two-screen model was checked with and is
    // still worth asking of a fixed-length file: nothing in it may refuse a saturated value.
    var data = new byte[SuperHiresEditorFile.FileSize];
    Array.Fill(data, (byte)0xFF);

    var original = SuperHiresEditorReader.FromBytes(data);
    var restored = SuperHiresEditorReader.FromBytes(SuperHiresEditorWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.LoadAddress, Is.EqualTo((ushort)0xFFFF));
      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
      Assert.That(restored.ScreenData, Is.EqualTo(original.ScreenData));
      Assert.That(restored.Sprites, Is.EqualTo(original.Sprites));
      Assert.That(restored.BackSpriteColor, Is.EqualTo(original.BackSpriteColor));
      Assert.That(restored.FrontSpriteColor, Is.EqualTo(original.FrontSpriteColor));
      Assert.That(restored.Trailer, Is.EqualTo(original.Trailer));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_PreservesData() {
    var original = SuperHiresEditorReader.FromBytes(_BuildValidFile(0x2000));
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".she");

    try {
      File.WriteAllBytes(path, SuperHiresEditorWriter.ToBytes(original));
      var restored = SuperHiresEditorReader.FromFile(new FileInfo(path));

      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }

  /// <summary>A logo with nothing on either sprite layer, so the bitmap is what shows.</summary>
  private static byte[] _BuildValidFile(ushort loadAddress) {
    var data = new byte[SuperHiresEditorFile.FileSize];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);

    for (var cell = 0; cell < SuperHiresEditorFile.ScreenSize; ++cell)
      data[SuperHiresEditorFile.ScreenOffset + cell] = 0x10;

    return data;
  }
}
