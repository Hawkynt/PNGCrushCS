using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.HiresInterlaceFeniks.Tests;

/// <summary>
/// The layout a Hires Interlace file actually has, as opposed to the one it used to be written in.
/// </summary>
/// <remarks>
/// What was written was 18002 bytes of bitmap, screen, bitmap, screen. The format is 24578, and the
/// four parts are in neither that order nor packed: the first bitmap at 2, the second field's video
/// matrix at 9218, the first field's at 10242 and the second bitmap at 16386.
/// </remarks>
[TestFixture]
public sealed class HiresInterlaceFeniksLayoutTests {

  private static byte[] _Build() {
    var data = new byte[HiresInterlaceFeniksFile.FileSize];
    data[0] = 0x00;
    data[1] = 0x20;

    for (var cell = 0; cell < HiresInterlaceFeniksFile.ScreenRamSize; ++cell) {
      data[HiresInterlaceFeniksFile.FirstScreenOffset + cell] = 0x10;
      data[HiresInterlaceFeniksFile.SecondScreenOffset + cell] = 0x20;
    }

    for (var i = 0; i < HiresInterlaceFeniksFile.BitmapDataSize; ++i) {
      data[HiresInterlaceFeniksFile.FirstBitmapOffset + i] = 0xFF;
      data[HiresInterlaceFeniksFile.SecondBitmapOffset + i] = 0xFF;
    }

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => HiresInterlaceFeniksReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_Throws()
    => Assert.Throws<FileNotFoundException>(
      () => HiresInterlaceFeniksReader.FromFile(new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hlf"))));

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => HiresInterlaceFeniksReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void TheOldFabricatedLength_IsNoLongerWhatItWants()
    => Assert.Throws<InvalidDataException>(() => HiresInterlaceFeniksReader.FromBytes(new byte[18002]));

  [Test]
  [Category("Unit")]
  public void ThePartsAreWhereTheMachineAddressesThem() {
    Assert.Multiple(() => {
      Assert.That(HiresInterlaceFeniksFile.FileSize, Is.EqualTo(24578));
      Assert.That(HiresInterlaceFeniksFile.FirstBitmapOffset, Is.EqualTo(2));
      Assert.That(HiresInterlaceFeniksFile.SecondScreenOffset, Is.EqualTo(9218));
      Assert.That(HiresInterlaceFeniksFile.FirstScreenOffset, Is.EqualTo(10242));
      Assert.That(HiresInterlaceFeniksFile.SecondBitmapOffset, Is.EqualTo(16386));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheTwoFieldsAreAveraged() {
    var picture = HiresInterlaceFeniksFile.ToRawImage(HiresInterlaceFeniksReader.FromBytes(_Build()));
    var white = Commodore64Graphics.HexColors[1];
    var red = Commodore64Graphics.HexColors[2];

    Assert.Multiple(() => {
      Assert.That(picture.Width, Is.EqualTo(320));
      Assert.That(picture.Height, Is.EqualTo(200));
      Assert.That(picture.PixelData[0], Is.EqualTo((byte)(((white >> 16) & 0xFF) + ((red >> 16) & 0xFF) >> 1)));
    });
  }

  [Test]
  [Category("Integration")]
  public void WhatIsReadIsWhatIsWritten() {
    var original = HiresInterlaceFeniksReader.FromBytes(_Build());
    var bytes = HiresInterlaceFeniksWriter.ToBytes(original);
    var restored = HiresInterlaceFeniksReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(HiresInterlaceFeniksFile.FileSize));
      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.FirstBitmap, Is.EqualTo(original.FirstBitmap));
      Assert.That(restored.FirstScreen, Is.EqualTo(original.FirstScreen));
      Assert.That(restored.SecondBitmap, Is.EqualTo(original.SecondBitmap));
      Assert.That(restored.SecondScreen, Is.EqualTo(original.SecondScreen));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var original = HiresInterlaceFeniksReader.FromBytes(_Build());
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hlf");

    try {
      File.WriteAllBytes(path, HiresInterlaceFeniksWriter.ToBytes(original));
      var restored = HiresInterlaceFeniksReader.FromFile(new FileInfo(path));

      Assert.That(restored.FirstBitmap, Is.EqualTo(original.FirstBitmap));
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => HiresInterlaceFeniksReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromStream_ReadsWhatFromBytesReads() {
    using var stream = new MemoryStream(_Build());
    var fromStream = HiresInterlaceFeniksFile.ToRawImage(HiresInterlaceFeniksReader.FromStream(stream));
    var fromBytes = HiresInterlaceFeniksFile.ToRawImage(HiresInterlaceFeniksReader.FromBytes(_Build()));

    Assert.Multiple(() => {
      Assert.That(fromStream.Width, Is.EqualTo(fromBytes.Width));
      Assert.That(fromStream.Height, Is.EqualTo(fromBytes.Height));
      Assert.That(fromStream.PixelData, Is.EqualTo(fromBytes.PixelData));
    });
  }
}
