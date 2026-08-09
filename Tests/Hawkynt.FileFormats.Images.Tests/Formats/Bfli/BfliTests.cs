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

  /// <summary>A file of the right length that opens with something else is not this format.</summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_RightLengthWrongHeader_ThrowsInvalidDataException() {
    var data = TestHelpers._BuildValidBfliData();
    data[2] = (byte)'a';

    Assert.Throws<InvalidDataException>(() => BfliReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidData_ParsesDimensions() {
    var result = BfliReader.FromBytes(TestHelpers._BuildValidBfliData());

    Assert.Multiple(() => {
      Assert.That(result.Width, Is.EqualTo(320));
      Assert.That(result.Height, Is.EqualTo(400));
      Assert.That(result.RawData.Length, Is.EqualTo(BfliFile.PayloadSize));
    });
  }
}

[TestFixture]
public sealed class BfliRoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var payload = new byte[BfliFile.PayloadSize];
    for (var i = 0; i < payload.Length; ++i)
      payload[i] = (byte)(i * 13 % 256);

    var original = new BfliFile { RawData = payload };

    var bytes = BfliWriter.ToBytes(original);
    var restored = BfliReader.FromBytes(bytes);

    Assert.Multiple(() => {
      Assert.That(bytes, Has.Length.EqualTo(BfliFile.FileSize));
      Assert.That(restored.RawData, Is.EqualTo(original.RawData));
    });
  }
}

file static class TestHelpers {
  internal static byte[] _BuildValidBfliData() {
    var data = new byte[BfliFile.FileSize];
    data[0] = 0xFF;
    data[1] = 0x3B;
    data[2] = (byte)'b';
    for (var i = BfliFile.HeaderSize; i < data.Length; ++i)
      data[i] = (byte)(i % 256);

    return data;
  }
}
