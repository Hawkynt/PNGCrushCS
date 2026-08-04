using System;
using System.IO;
using FileFormat.MicroIllustrator;

namespace FileFormat.MicroIllustrator.Tests;

/// <summary>
/// What a Micro Illustrator file is.
/// </summary>
/// <remarks>
/// These used to build a 10003-byte buffer laid out as bitmap, matrix, colour and a background byte
/// at the end — a plain Koala screen under another name, and a layout no Micro Illustrator file has.
/// Both samples in the corpus were refused for being the wrong length, and RECOIL accepts nothing
/// but 10022. A real file keeps a twenty-byte header after the load address, holds the background in
/// it, and then gives the matrix, the colour memory and the bitmap in that order.
/// </remarks>
[TestFixture]
public sealed class MicroIllustratorReaderTests {

  /// <summary>Builds a file of the real shape, its sections filled with values that tell them apart.</summary>
  private static byte[] _BuildValidFile(ushort loadAddress, byte background) {
    var data = new byte[MicroIllustratorFile.ExpectedFileSize];

    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);
    data[MicroIllustratorFile.HeaderSizeOffset] = MicroIllustratorFile.HeaderSize;
    data[MicroIllustratorFile.BackgroundOffset] = background;

    var at = MicroIllustratorFile.PictureOffset;
    for (var i = 0; i < 1000; ++i)
      data[at + i] = 0x11;
    for (var i = 0; i < 1000; ++i)
      data[at + 1000 + i] = 0x22;
    for (var i = 0; i < 8000; ++i)
      data[at + 2000 + i] = 0x33;

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MicroIllustratorReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MicroIllustratorReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mil"));

    Assert.Throws<FileNotFoundException>(() => MicroIllustratorReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => MicroIllustratorReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => MicroIllustratorReader.FromBytes(new byte[100]));

  [Test]
  [Category("Unit")]
  public void FromBytes_TheOldFabricatedSize_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => MicroIllustratorReader.FromBytes(new byte[10003]));

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidParsesCorrectly() {
    var result = MicroIllustratorReader.FromBytes(_BuildValidFile(0x18DC, 0x03));

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(160));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.LoadAddress, Is.EqualTo(0x18DC));
      Assert.That(result.BitmapData, Has.Length.EqualTo(8000));
      Assert.That(result.VideoMatrix, Has.Length.EqualTo(1000));
      Assert.That(result.ColorRam, Has.Length.EqualTo(1000));
      Assert.That(result.BackgroundColor, Is.EqualTo(0x03), "which sits in the header, not at the end");
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TakesTheSectionsMatrixFirstAndBitmapLast() {
    var result = MicroIllustratorReader.FromBytes(_BuildValidFile(0x18DC, 0));

    // Read in the order every other C64 picture here uses, the bitmap would be the 0x11 section.
    Assert.Multiple(() => {
      Assert.That(result.VideoMatrix[0], Is.EqualTo(0x11));
      Assert.That(result.ColorRam[0], Is.EqualTo(0x22));
      Assert.That(result.BitmapData[0], Is.EqualTo(0x33));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_AZeroedHeaderIsStillRead() {
    // One of the two samples states nought for its own header length and RECOIL draws it from the
    // same place regardless, so the field describes the header rather than pointing past it.
    var data = _BuildValidFile(0x18DC, 0);
    data[MicroIllustratorFile.HeaderSizeOffset] = 0;

    Assert.That(MicroIllustratorReader.FromBytes(data).BitmapData[0], Is.EqualTo(0x33));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidParsesCorrectly() {
    using var ms = new MemoryStream(_BuildValidFile(0x18DC, 0x05));

    var result = MicroIllustratorReader.FromStream(ms);

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(160));
      Assert.That(result.Height, Is.EqualTo(200));
      Assert.That(result.BackgroundColor, Is.EqualTo(0x05));
    });
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_WhatIsWrittenComesBackTheSame() {
    var original = MicroIllustratorReader.FromBytes(_BuildValidFile(0x18DC, 0x07));

    var restored = MicroIllustratorReader.FromBytes(MicroIllustratorWriter.ToBytes(original));

    Assert.Multiple(() => {
      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
      Assert.That(restored.VideoMatrix, Is.EqualTo(original.VideoMatrix));
      Assert.That(restored.ColorRam, Is.EqualTo(original.ColorRam));
      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IsTheLengthARealFileHas() {
    var file = MicroIllustratorReader.FromBytes(_BuildValidFile(0x18DC, 0));

    Assert.That(MicroIllustratorWriter.ToBytes(file), Has.Length.EqualTo(10022));
  }
}
