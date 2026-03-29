using System;
using System.IO;
using FileFormat.Core;
using FileFormat.Icns;

namespace FileFormat.Icns.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void IcnsEntry_FieldsRoundTrip() {
    var data = new byte[] { 1, 2, 3 };
    var entry = new IcnsEntry("ic07", data, 128, 128);

    Assert.That(entry.OsType, Is.EqualTo("ic07"));
    Assert.That(entry.Data, Is.SameAs(data));
    Assert.That(entry.Width, Is.EqualTo(128));
    Assert.That(entry.Height, Is.EqualTo(128));
  }

  [Test]
  [Category("Unit")]
  public void IcnsEntry_IsPng_TrueForModernTypes() {
    Assert.That(new IcnsEntry("ic07", [], 128, 128).IsPng, Is.True);
    Assert.That(new IcnsEntry("ic08", [], 256, 256).IsPng, Is.True);
    Assert.That(new IcnsEntry("ic09", [], 512, 512).IsPng, Is.True);
    Assert.That(new IcnsEntry("ic10", [], 1024, 1024).IsPng, Is.True);
    Assert.That(new IcnsEntry("ic11", [], 32, 32).IsPng, Is.True);
    Assert.That(new IcnsEntry("ic12", [], 64, 64).IsPng, Is.True);
    Assert.That(new IcnsEntry("ic13", [], 256, 256).IsPng, Is.True);
    Assert.That(new IcnsEntry("ic14", [], 512, 512).IsPng, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void IcnsEntry_IsPng_FalseForLegacyTypes() {
    Assert.That(new IcnsEntry("is32", [], 16, 16).IsPng, Is.False);
    Assert.That(new IcnsEntry("s8mk", [], 16, 16).IsPng, Is.False);
    Assert.That(new IcnsEntry("ICN#", [], 32, 32).IsPng, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void IcnsEntry_IsLegacyRgb_TrueForRgbTypes() {
    Assert.That(new IcnsEntry("is32", [], 16, 16).IsLegacyRgb, Is.True);
    Assert.That(new IcnsEntry("il32", [], 32, 32).IsLegacyRgb, Is.True);
    Assert.That(new IcnsEntry("ih32", [], 48, 48).IsLegacyRgb, Is.True);
    Assert.That(new IcnsEntry("it32", [], 128, 128).IsLegacyRgb, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void IcnsEntry_IsLegacyRgb_FalseForOtherTypes() {
    Assert.That(new IcnsEntry("ic07", [], 128, 128).IsLegacyRgb, Is.False);
    Assert.That(new IcnsEntry("s8mk", [], 16, 16).IsLegacyRgb, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void IcnsEntry_IsLegacyMask_TrueForMaskTypes() {
    Assert.That(new IcnsEntry("s8mk", [], 16, 16).IsLegacyMask, Is.True);
    Assert.That(new IcnsEntry("l8mk", [], 32, 32).IsLegacyMask, Is.True);
    Assert.That(new IcnsEntry("h8mk", [], 48, 48).IsLegacyMask, Is.True);
    Assert.That(new IcnsEntry("t8mk", [], 128, 128).IsLegacyMask, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void IcnsEntry_IsLegacyMask_FalseForOtherTypes() {
    Assert.That(new IcnsEntry("ic07", [], 128, 128).IsLegacyMask, Is.False);
    Assert.That(new IcnsEntry("is32", [], 16, 16).IsLegacyMask, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void IcnsEntry_Is1Bit_TrueFor1BitTypes() {
    Assert.That(new IcnsEntry("ICN#", [], 32, 32).Is1Bit, Is.True);
    Assert.That(new IcnsEntry("icm#", [], 16, 12).Is1Bit, Is.True);
    Assert.That(new IcnsEntry("ics#", [], 16, 16).Is1Bit, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void IcnsEntry_Is1Bit_FalseForOtherTypes() {
    Assert.That(new IcnsEntry("ic07", [], 128, 128).Is1Bit, Is.False);
    Assert.That(new IcnsEntry("is32", [], 16, 16).Is1Bit, Is.False);
  }

  [Test]
  [Category("Unit")]
  public void IcnsEntry_HeaderSize_Is8() {
    Assert.That(IcnsEntry.HeaderSize, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void IcnsFile_DefaultEntries_IsEmptyList() {
    var file = new IcnsFile();
    Assert.That(file.Entries, Is.Not.Null);
    Assert.That(file.Entries, Has.Count.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void IcnsFile_PrimaryExtension_IsIcns() {
    var ext = _GetPrimaryExtension();
    Assert.That(ext, Is.EqualTo(".icns"));
  }

  [Test]
  [Category("Unit")]
  public void IcnsFile_FileExtensions_ContainsIcns() {
    var extensions = _GetFileExtensions();
    Assert.That(extensions, Has.Length.EqualTo(1));
    Assert.That(extensions[0], Is.EqualTo(".icns"));
  }

  private static string _GetPrimaryExtension() => _GetPrimary<IcnsFile>();
  private static string _GetPrimary<T>() where T : IImageFileFormat<T> => T.PrimaryExtension;
  private static string[] _GetFileExtensions() => _GetExts<IcnsFile>();
  private static string[] _GetExts<T>() where T : IImageFileFormat<T> => T.FileExtensions;

  [Test]
  [Category("Unit")]
  public void IcnsFile_ToRawImage_NullThrows() {
    Assert.Throws<ArgumentNullException>(() => IcnsFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void IcnsFile_ToRawImage_EmptyEntriesThrows() {
    var file = new IcnsFile { Entries = [] };
    Assert.Throws<InvalidDataException>(() => IcnsFile.ToRawImage(file));
  }

  [Test]
  [Category("Unit")]
  public void IcnsFile_FromRawImage_NullThrows() {
    Assert.Throws<ArgumentNullException>(() => IcnsFile.FromRawImage(null!));
  }
}
