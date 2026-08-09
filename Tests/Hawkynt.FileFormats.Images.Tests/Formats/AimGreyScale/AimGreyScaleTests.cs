using System;
using System.IO;
using FileFormat.Core;

namespace FileFormat.AimGreyScale.Tests;

/// <summary>
/// AIM grey scale: a file of nothing but samples, described by a companion beside it.
/// </summary>
/// <remarks>
/// Both the companion's layout and the fallback were settled by building files and handing them to
/// XnView's own converter: the same picture reads at two different shapes when only the companion
/// changes, and the one length that needs no companion is 65,536 bytes.
/// <para/>
/// There is no signature. A reader for this name once stood here requiring "AIM\0" and could not have
/// read a single real file; nothing below looks at the picture's content at all, because the loader
/// does not either.
/// </remarks>
[TestFixture]
public sealed class AimGreyScaleTests {

  /// <summary>A companion stating a size: two characters at four, then the size at 0x16.</summary>
  private static byte[] _Companion(int width, int height, string mark = "AA") {
    var data = new byte[AimGreyScaleFile.CompanionSize];
    data[4] = (byte)mark[0];
    data[5] = (byte)mark[1];
    data[0x16] = (byte)(width >> 8);
    data[0x17] = (byte)width;
    data[0x18] = (byte)(height >> 8);
    data[0x19] = (byte)height;
    return data;
  }

  /// <summary>Writes a picture and, when there is one, its companion, then reads the picture back.</summary>
  private static AimGreyScaleFile _Beside(byte[] pixels, byte[]? companion, string name = "scan.ima") {
    var directory = Directory.CreateTempSubdirectory("aim");
    try {
      var path = Path.Combine(directory.FullName, name);
      File.WriteAllBytes(path, pixels);
      if (companion != null)
        File.WriteAllBytes(Path.ChangeExtension(path, AimGreyScaleFile.CompanionExtension), companion);

      return AimGreyScaleReader.FromFile(new FileInfo(path));
    } finally {
      directory.Delete(recursive: true);
    }
  }

  [Test]
  [Category("Integration")]
  public void Read_TakesItsSizeFromTheCompanionAndItsPixelsAsTheyLie() {
    byte[] pixels = [0x00, 0x40, 0x80, 0xC0, 0xFF, 0x10];
    var image = AimGreyScaleFile.ToRawImage(_Beside(pixels, _Companion(3, 2)));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(3));
      Assert.That(image.Height, Is.EqualTo(2));
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Gray8));
      Assert.That(image.PixelData, Is.EqualTo(pixels), "top-down, zero is black, nothing skipped");
    });
  }

  /// <summary>
  /// The same picture reads at two shapes when only the file beside it changes.
  /// </summary>
  [Test]
  [Category("Integration")]
  public void Read_ReadsOnePictureAtTwoShapesDependingOnlyOnTheCompanion() {
    var pixels = new byte[28];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 9);

    var wide = _Beside(pixels, _Companion(7, 4));
    var tall = _Beside(pixels, _Companion(4, 7));

    Assert.Multiple(() => {
      Assert.That((wide.Width, wide.Height), Is.EqualTo((7, 4)));
      Assert.That((tall.Width, tall.Height), Is.EqualTo((4, 7)));
      Assert.That(wide.PixelData, Is.EqualTo(pixels));
      Assert.That(tall.PixelData, Is.EqualTo(pixels));
    });
  }

  [Test]
  [Category("Integration")]
  public void Read_FallsBackOnTheOneLengthThatNeedsNoCompanion() {
    var file = _Beside(new byte[AimGreyScaleFile.FallbackLength], companion: null);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(256));
      Assert.That(file.Height, Is.EqualTo(256));
    });
  }

  [TestCase("aa")]
  [TestCase("AB")]
  [Category("Integration")]
  public void Read_IgnoresACompanionWithoutTheTwoCharactersItMustCarry(string mark)
    => Assert.Throws<InvalidDataException>(() => _Beside(new byte[6], _Companion(3, 2, mark)));

  /// <summary>
  /// A companion describing some other number of pixels is not this picture's and is passed over.
  /// </summary>
  [Test]
  [Category("Integration")]
  public void Read_IgnoresACompanionThatDoesNotAccountForThePictureExactly() {
    var file = _Beside(new byte[AimGreyScaleFile.FallbackLength], _Companion(10, 10));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(256));
      Assert.That(file.Height, Is.EqualTo(256));
    });
  }

  [Test]
  [Category("Integration")]
  public void Read_TakesTheCompanionForANameCarryingMoreThanOneDot() {
    var file = _Beside(new byte[6], _Companion(3, 2), "study.01.ima");

    Assert.That((file.Width, file.Height), Is.EqualTo((3, 2)));
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesEveryOtherLengthWhenThereIsNoCompanion()
    => Assert.Throws<InvalidDataException>(() => _Beside(new byte[4096], companion: null));

  [Test]
  [Category("Unit")]
  public void Read_FromBytesReachesNoCompanionAndSoReadsOnlyTheOneSize() {
    Assert.Multiple(() => {
      Assert.That(AimGreyScaleReader.FromBytes(new byte[AimGreyScaleFile.FallbackLength]).Width, Is.EqualTo(256));
      Assert.Throws<InvalidDataException>(() => AimGreyScaleReader.FromBytes(new byte[65535]));
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesACompanionShorterThanTheFieldsItIsReadFor() {
    var stunted = _Companion(3, 2)[..25];

    Assert.Throws<InvalidDataException>(() => AimGreyScaleReader.FromBytesAndCompanion(new byte[6], stunted));
  }

  private static RawImage _Grey(int width, int height) {
    var pixels = new byte[width * height];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 5 + 3);

    return new() { Width = width, Height = height, Format = PixelFormat.Gray8, PixelData = pixels };
  }

  /// <summary>Writes a picture through the path, then reads back whatever ended up in the directory.</summary>
  private static (AimGreyScaleFile File, bool Companion) _Written(RawImage image, string name = "scan.ima") {
    var directory = Directory.CreateTempSubdirectory("aim");
    try {
      var target = new FileInfo(Path.Combine(directory.FullName, name));
      FormatIO.WriteToFile<AimGreyScaleFile>(image, target);

      return (AimGreyScaleReader.FromFile(target),
        File.Exists(Path.ChangeExtension(target.FullName, AimGreyScaleFile.CompanionExtension)));
    } finally {
      directory.Delete(recursive: true);
    }
  }

  /// <summary>
  /// A size that is not 256 by 256 comes back only because the companion was written too.
  /// </summary>
  /// <remarks>
  /// This is the whole of what makes the format writable. The picture file states nothing, so without
  /// the <c>.hd</c> beside it a 48 by 20 picture is 960 bytes that no reader can place — refused here
  /// and refused by the converter, which was checked by handing it both files and then only one.
  /// </remarks>
  [Test]
  [Category("Integration")]
  public void Write_PutsTheSizeInTheCompanionSoAnyShapeReadsBack() {
    var source = _Grey(48, 20);
    var (file, companion) = _Written(source);

    Assert.Multiple(() => {
      Assert.That(companion, Is.True, "the .hd was written");
      Assert.That((file.Width, file.Height), Is.EqualTo((48, 20)));
      Assert.That(file.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void Write_NamesTheCompanionAfterThePictureEvenWithMoreThanOneDot() {
    var (file, companion) = _Written(_Grey(7, 4), "study.01.ima");

    Assert.Multiple(() => {
      Assert.That(companion, Is.True);
      Assert.That((file.Width, file.Height), Is.EqualTo((7, 4)));
    });
  }

  /// <summary>The companion this writes is one this reader accepts, field for field.</summary>
  [Test]
  [Category("Unit")]
  public void Write_BuildsACompanionShapedTheWayTheLoaderWantsIt() {
    var written = AimGreyScaleWriter.CompanionBytes(AimGreyScaleFile.FromRawImage(_Grey(300, 7)));

    Assert.That(written, Is.EqualTo(_Companion(300, 7)));
  }

  /// <summary>Bytes alone reach no companion, so only the one fallback size survives that route.</summary>
  [Test]
  [Category("Unit")]
  public void Write_ToBytesAloneIsReadableOnlyAtTheSizeThatNeedsNoCompanion() {
    Assert.Multiple(() => {
      Assert.That(FormatIO.Encode<AimGreyScaleFile>(_Grey(256, 256)), Has.Length.EqualTo(AimGreyScaleFile.FallbackLength));
      Assert.DoesNotThrow(() => AimGreyScaleReader.FromBytes(FormatIO.Encode<AimGreyScaleFile>(_Grey(256, 256))));
      Assert.Throws<InvalidDataException>(() => AimGreyScaleReader.FromBytes(FormatIO.Encode<AimGreyScaleFile>(_Grey(48, 20))));
    });
  }
}
