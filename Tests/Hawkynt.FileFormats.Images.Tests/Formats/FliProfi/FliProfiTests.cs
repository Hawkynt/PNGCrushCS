using System;
using System.IO;
using FileFormat.FliProfi;
using FileFormat.Core;

namespace FileFormat.FliProfi.Tests;

[TestFixture]
public sealed class FliProfiReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FliProfiReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fpr"));
    Assert.Throws<FileNotFoundException>(() => FliProfiReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FliProfiReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => FliProfiReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => FliProfiReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = TestHelpers._BuildValidFliProfiData(0x3C00);
    var result = FliProfiReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(160));
    Assert.That(result.Height, Is.EqualTo(200));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesLoadAddress() {
    var data = TestHelpers._BuildValidFliProfiData(0x4000);
    var result = FliProfiReader.FromBytes(data);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_RawDataLength() {
    var data = TestHelpers._BuildValidFliProfiData(0x3C00);
    var result = FliProfiReader.FromBytes(data);

    Assert.That(result.RawData.Length, Is.EqualTo(data.Length - FliProfiFile.LoadAddressSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_RawDataPreserved() {
    var data = TestHelpers._BuildValidFliProfiData(0x3C00);
    var result = FliProfiReader.FromBytes(data);

    for (var i = 0; i < result.RawData.Length; ++i)
      Assert.That(result.RawData[i], Is.EqualTo(data[i + FliProfiFile.LoadAddressSize]), $"RawData mismatch at index {i}");
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_MinimumSize_Accepted() {
    var minSize = FliProfiFile.LoadAddressSize + FliProfiFile.MinPayloadSize;
    var data = new byte[minSize];
    data[0] = 0x00;
    data[1] = 0x3C;

    var result = FliProfiReader.FromBytes(data);
    Assert.That(result.RawData.Length, Is.EqualTo(FliProfiFile.MinPayloadSize));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_JustBelowMinimumSize_ThrowsInvalidDataException() {
    var tooSmall = FliProfiFile.LoadAddressSize + FliProfiFile.MinPayloadSize - 1;
    Assert.Throws<InvalidDataException>(() => FliProfiReader.FromBytes(new byte[tooSmall]));
  }
}

[TestFixture]
public sealed class FliProfiRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var rawData = new byte[FliProfiFile.MinPayloadSize + 500];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new FliProfiFile { LoadAddress = 0x3C00, RawData = rawData };

    var bytes = FliProfiWriter.ToBytes(original);
    var restored = FliProfiReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_PreservesData() {
    var rawData = new byte[FliProfiFile.MinPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var original = new FliProfiFile { LoadAddress = 0x4000, RawData = rawData };

    var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".fpr");
    try {
      File.WriteAllBytes(tmp, FliProfiWriter.ToBytes(original));
      var restored = FliProfiReader.FromFile(new FileInfo(tmp));

      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    } finally {
      try { File.Delete(tmp); } catch { /* best effort */ }
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaStream_PreservesData() {
    var rawData = new byte[FliProfiFile.MinPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var original = new FliProfiFile { LoadAddress = 0x3C00, RawData = rawData };
    var bytes = FliProfiWriter.ToBytes(original);

    using var ms = new MemoryStream(bytes);
    var restored = FliProfiReader.FromStream(ms);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllZeros() {
    var rawData = new byte[FliProfiFile.MinPayloadSize];
    var original = new FliProfiFile { LoadAddress = 0x0000, RawData = rawData };

    var bytes = FliProfiWriter.ToBytes(original);
    var restored = FliProfiReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0));
    Assert.That(restored.RawData, Is.EqualTo(rawData));
  }
}

file static class TestHelpers {
  internal static byte[] _BuildValidFliProfiData(ushort loadAddress) {
    var rawData = new byte[FliProfiFile.MinPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var data = new byte[FliProfiFile.LoadAddressSize + rawData.Length];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);
    Array.Copy(rawData, 0, data, FliProfiFile.LoadAddressSize, rawData.Length);
    return data;
  }
}
