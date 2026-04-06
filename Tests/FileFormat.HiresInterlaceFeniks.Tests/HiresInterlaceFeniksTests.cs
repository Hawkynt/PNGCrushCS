using System;
using System.IO;
using FileFormat.HiresInterlaceFeniks;
using FileFormat.Core;

namespace FileFormat.HiresInterlaceFeniks.Tests;

[TestFixture]
public sealed class HiresInterlaceFeniksReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HiresInterlaceFeniksReader.FromFile(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hlf"));
    Assert.Throws<FileNotFoundException>(() => HiresInterlaceFeniksReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HiresInterlaceFeniksReader.FromBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException() {
    Assert.Throws<InvalidDataException>(() => HiresInterlaceFeniksReader.FromBytes(new byte[100]));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var data = TestHelpers._BuildValidHiresInterlaceFeniksData(0x3C00);
    var result = HiresInterlaceFeniksReader.FromBytes(data);

    Assert.That(result.Width, Is.EqualTo(320));
    Assert.That(result.Height, Is.EqualTo(200));
    Assert.That(result.LoadAddress, Is.EqualTo(0x3C00));
    Assert.That(result.RawData.Length, Is.GreaterThanOrEqualTo(HiresInterlaceFeniksFile.MinPayloadSize));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => HiresInterlaceFeniksReader.FromStream(null!));
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_ParsesCorrectly() {
    var data = TestHelpers._BuildValidHiresInterlaceFeniksData(0x4000);
    using var ms = new MemoryStream(data);
    var result = HiresInterlaceFeniksReader.FromStream(ms);

    Assert.That(result.LoadAddress, Is.EqualTo(0x4000));
    Assert.That(result.RawData.Length, Is.GreaterThanOrEqualTo(HiresInterlaceFeniksFile.MinPayloadSize));
  }
}

[TestFixture]
public sealed class HiresInterlaceFeniksRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var rawData = new byte[HiresInterlaceFeniksFile.MinPayloadSize + 500];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i * 13 % 256);

    var original = new HiresInterlaceFeniksFile { LoadAddress = 0x3C00, RawData = rawData };

    var bytes = HiresInterlaceFeniksWriter.ToBytes(original);
    var restored = HiresInterlaceFeniksReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var rawData = new byte[HiresInterlaceFeniksFile.MinPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var original = new HiresInterlaceFeniksFile { LoadAddress = 0x4000, RawData = rawData };
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hlf");
    try {
      File.WriteAllBytes(path, HiresInterlaceFeniksWriter.ToBytes(original));
      var restored = HiresInterlaceFeniksReader.FromFile(new FileInfo(path));

      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    } finally {
      if (File.Exists(path))
        File.Delete(path);
    }
  }
}

file static class TestHelpers {
  internal static byte[] _BuildValidHiresInterlaceFeniksData(ushort loadAddress) {
    var rawData = new byte[HiresInterlaceFeniksFile.MinPayloadSize];
    for (var i = 0; i < rawData.Length; ++i)
      rawData[i] = (byte)(i % 256);

    var data = new byte[HiresInterlaceFeniksFile.LoadAddressSize + rawData.Length];
    data[0] = (byte)(loadAddress & 0xFF);
    data[1] = (byte)(loadAddress >> 8);
    Array.Copy(rawData, 0, data, HiresInterlaceFeniksFile.LoadAddressSize, rawData.Length);
    return data;
  }
}
