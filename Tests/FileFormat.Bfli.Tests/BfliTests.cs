using System;
using System.IO;
using FileFormat.Bfli;
using FileFormat.Core;

namespace FileFormat.Bfli.Tests;

[TestFixture]
public sealed class BfliReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BfliReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bfl"));
    Assert.Throws<FileNotFoundException>(() => BfliReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BfliReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => BfliReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = TestHelpers._BuildValidBfliData(0x3C00);
    var result = BfliReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x3C00));
    Assert.That(result.RawData.Length, Is.GreaterThanOrEqualTo(8000));
  }
}

[TestFixture]
public sealed class BfliWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BfliWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CorrectOutputSize() {
    var rawData = new byte[9000];
    var file = new BfliFile { LoadAddress = 0x3C00, RawData = rawData };
    var bytes = BfliWriter.ToBytes(file);

    Assert.That(bytes.Length, Is.EqualTo(2 + 9000));
  }
}

[TestFixture]
public sealed class BfliRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var rawData = new byte[33000];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new BfliFile { LoadAddress = 0x3C00, RawData = rawData };

    var bytes = BfliWriter.ToBytes(original);
    var restored = BfliReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }
}

[TestFixture]
public sealed class BfliDataTypeTests {

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsBfl() {
    Assert.That(_GetPrimaryExtension(), Is.EqualTo(".bfl"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsBflAndBfli() {
    var extensions = _GetFileExtensions();
    Assert.That(extensions, Does.Contain(".bfl"));
    Assert.That(extensions, Does.Contain(".bfli"));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BfliFile.ToRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => BfliFile.FromRawImage(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_ThrowsNotSupportedException() {
    var image = new RawImage { Width = 320, Height = 200, Format = PixelFormat.Rgb24, PixelData = new byte[320 * 200 * 3] };
    Assert.Throws<NotSupportedException>(() => BfliFile.FromRawImage(image));
  }

  private static string _GetPrimaryExtension() => _Helper<BfliFile>.PrimaryExtension;
  private static string[] _GetFileExtensions() => _Helper<BfliFile>.FileExtensions;

  private static class _Helper<T> where T : IImageFileFormat<T> {
    public static string PrimaryExtension => T.PrimaryExtension;
    public static string[] FileExtensions => T.FileExtensions;
  }
}

file static class TestHelpers {
  internal static byte[] _BuildValidBfliData(ushort loadAddress) {
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
