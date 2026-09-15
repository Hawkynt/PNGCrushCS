using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.MultiLaceEditor.Tests;

/// <summary>
/// What a Multi-Lace Editor logo is.
/// </summary>
/// <remarks>
/// These used to build two 160 by 200 multicolour screens with video matrices and a shared colour
/// memory, 19003 bytes — the shape of a Koala picture doubled. The format is 4098: 320 by 56, two
/// fields of 2048 bytes at two bits a pixel against four colours the program has and the file does
/// not store, the displaced field first.
/// </remarks>
[TestFixture]
public sealed class MultiLaceEditorLayoutTests {

  private static byte[] _Build() {
    var data = new byte[MultiLaceEditorFile.FileSize];
    data[0] = 0x00;
    data[1] = 0x08;

    // Patterns 0,1,2,3 across the first four pixel pairs of the first field.
    data[MultiLaceEditorFile.FirstFieldOffset] = 0b00_01_10_11;
    data[MultiLaceEditorFile.SecondFieldOffset] = 0b00_01_10_11;

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => MultiLaceEditorReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_Throws()
    => Assert.Throws<FileNotFoundException>(
      () => MultiLaceEditorReader.FromFile(new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mle"))));

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => MultiLaceEditorReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void AnythingShorterThanTwoFieldsIsRefused()
    => Assert.Throws<InvalidDataException>(() => MultiLaceEditorReader.FromBytes(new byte[4097]));

  [Test]
  [Category("Unit")]
  public void ThePartsAreWhereTheMachineAddressesThem() {
    Assert.Multiple(() => {
      Assert.That(MultiLaceEditorFile.FileSize, Is.EqualTo(4098));
      Assert.That(MultiLaceEditorFile.SecondFieldOffset, Is.EqualTo(2));
      Assert.That(MultiLaceEditorFile.FirstFieldOffset, Is.EqualTo(2050));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheLogoIsThreeHundredAndTwentyByFiftySix() {
    var picture = MultiLaceEditorFile.ToRawImage(MultiLaceEditorReader.FromBytes(_Build()));

    Assert.Multiple(() => {
      Assert.That(picture.Width, Is.EqualTo(320));
      Assert.That(picture.Height, Is.EqualTo(56));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheFourColoursAreTheEditorsOwn() {
    // Pattern 3 is green, which is neither the third entry of the machine's palette nor anything the
    // file states: the program has these four and no others.
    var data = _Build();
    for (var i = 0; i < MultiLaceEditorFile.FieldSize; ++i) {
      data[MultiLaceEditorFile.FirstFieldOffset + i] = 0xFF;
      data[MultiLaceEditorFile.SecondFieldOffset + i] = 0xFF;
    }

    var picture = MultiLaceEditorFile.ToRawImage(MultiLaceEditorReader.FromBytes(data));
    var green = Commodore64Graphics.HexColors[MultiLaceEditorFile.ColorIndices[3]];

    Assert.That(picture.PixelData[2 * 3], Is.EqualTo((byte)(green >> 16)));
  }

  [Test]
  [Category("Unit")]
  public void TheBottomRightIsBlackBecauseTheFieldRunsOut() {
    // Seven rows of forty cells is 280 and a field holds 256, so the last 24 cells have nothing.
    var data = _Build();
    for (var i = 0; i < MultiLaceEditorFile.FieldSize; ++i) {
      data[MultiLaceEditorFile.FirstFieldOffset + i] = 0xFF;
      data[MultiLaceEditorFile.SecondFieldOffset + i] = 0xFF;
    }

    var picture = MultiLaceEditorFile.ToRawImage(MultiLaceEditorReader.FromBytes(data));

    Assert.That(picture.PixelData[(50 * 320 + 300) * 3], Is.Zero);
  }

  [Test]
  [Category("Integration")]
  public void WhatIsReadIsWhatIsWritten() {
    var original = MultiLaceEditorReader.FromBytes(_Build());
    var bytes = MultiLaceEditorWriter.ToBytes(original);
    var restored = MultiLaceEditorReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(MultiLaceEditorFile.FileSize));
      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.FirstField, Is.EqualTo(original.FirstField));
      Assert.That(restored.SecondField, Is.EqualTo(original.SecondField));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = MultiLaceEditorReader.FromBytes(_Build());
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mle");

    try {
      File.WriteAllBytes(path, MultiLaceEditorWriter.ToBytes(original));
      var restored = MultiLaceEditorReader.FromFile(new FileInfo(path));

      Assert.That(restored.FirstField, Is.EqualTo(original.FirstField));
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }
}
