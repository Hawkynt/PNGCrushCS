using System;
using NUnit.Framework;
using FileFormat.Afli;

namespace FileFormat.Afli.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new AfliFile {
      LoadAddress = 0x2000,
      RawData = new byte[AfliFile.ExpectedFileSize - 2],
    };
    var bytes = AfliWriter.ToBytes(original);
    var roundTripped = AfliReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(roundTripped.RawData, Is.EqualTo(original.RawData));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => AfliReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => AfliWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
