using System;
using System.IO;
using FileFormat.HiPicCreator;
using FileFormat.Core;

namespace FileFormat.HiPicCreator.Tests;

[TestFixture]
public sealed class HiPicCreatorReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HiPicCreatorReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hpc"));
    Assert.Throws<FileNotFoundException>(() => HiPicCreatorReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HiPicCreatorReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => HiPicCreatorReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = TestHelpers._BuildValidHpcData(0x4000);
    var result = HiPicCreatorReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
    Assert.That(result.RawData.Length, Is.GreaterThanOrEqualTo(10000));
  }
}

[TestFixture]
public sealed class HiPicCreatorWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HiPicCreatorWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectOutputSize() {
    var rawData = new byte[10000];
    var file = new HiPicCreatorFile { LoadAddress = 0x4000, RawData = rawData };
    var bytes = HiPicCreatorWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(2 + 10000));
  }
}

[TestFixture]
public sealed class HiPicCreatorRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var rawData = new byte[10000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new HiPicCreatorFile { LoadAddress = 0x4000, RawData = rawData };

    var bytes = HiPicCreatorWriter.ToBytes(original);
    var restored = HiPicCreatorReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }
}

[TestFixture]
public sealed class HiPicCreatorDataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsHpc() {
    Assert.That(_GetPrimaryExtension(), Is.EqualTo(".hpc"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsHpc() {
    var extensions = _GetFileExtensions();
    Assert.That(extensions, Does.Contain(".hpc"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HiPicCreatorFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HiPicCreatorFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new RawImage { Width = 160, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[160 * 200 * 3] };
    Assert.Throws<NotSupportedException>(() => HiPicCreatorFile.FromRawImage(image));
  }

  private static string _GetPrimaryExtension() => _Helper<HiPicCreatorFile>.PrimaryExtension;
  private static string[] _GetFileExtensions() => _Helper<HiPicCreatorFile>.FileExtensions;

  private static class _Helper<T> where T : IImageFileFormat<T> {
    public static string PrimaryExtension => T.PrimaryExtension;
    public static string[] FileExtensions => T.FileExtensions;
  }
}

file static class TestHelpers {
  internal static byte[] _BuildValidHpcData(ushort loadAddress) {
    var rawData = new byte[10000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var data = new byte[2 + rawData.Length];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);
    Array.Copy(rawData, 0, data, 2, rawData.Length);
    return data;
  }
}
