using System;
using System.IO;
using FileFormat.GigaPaint;
using FileFormat.Core;

namespace FileFormat.GigaPaint.Tests;

[TestFixture]
public sealed class GigaPaintReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GigaPaintReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gih"));
    Assert.Throws<FileNotFoundException>(() => GigaPaintReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GigaPaintReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => GigaPaintReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = TestHelpers._BuildValidGigaPaintData(0x2000);
    var result = GigaPaintReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x2000));
    Assert.That(result.RawData.Length, Is.GreaterThanOrEqualTo(8000));
  }
}

[TestFixture]
public sealed class GigaPaintWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GigaPaintWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectOutputSize() {
    var rawData = new byte[9000];
    var file = new GigaPaintFile { LoadAddress = 0x2000, RawData = rawData };
    var bytes = GigaPaintWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(2 + 9000));
  }
}

[TestFixture]
public sealed class GigaPaintRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var rawData = new byte[9000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 17 % 256);

    var original = new GigaPaintFile { LoadAddress = 0x2000, RawData = rawData };

    var bytes = GigaPaintWriter.ToBytes(original);
    var restored = GigaPaintReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }
}

[TestFixture]
public sealed class GigaPaintDataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsGih() {
    Assert.That(_GetPrimaryExtension(), Is.EqualTo(".gih"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsGihAndGig() {
    var extensions = _GetFileExtensions();
    Assert.That(extensions, Does.Contain(".gih"));
    Assert.That(extensions, Does.Contain(".gig"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GigaPaintFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => GigaPaintFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new RawImage { Width = 320, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[320 * 200 * 3] };
    Assert.Throws<NotSupportedException>(() => GigaPaintFile.FromRawImage(image));
  }

  private static string _GetPrimaryExtension() => _Helper<GigaPaintFile>.PrimaryExtension;
  private static string[] _GetFileExtensions() => _Helper<GigaPaintFile>.FileExtensions;

  private static class _Helper<T> where T : IImageFileFormat<T> {
    public static string PrimaryExtension => T.PrimaryExtension;
    public static string[] FileExtensions => T.FileExtensions;
  }
}

file static class TestHelpers {
  internal static byte[] _BuildValidGigaPaintData(ushort loadAddress) {
    var rawData = new byte[9000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var data = new byte[2 + rawData.Length];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);
    Array.Copy(rawData, 0, data, 2, rawData.Length);
    return data;
  }
}
