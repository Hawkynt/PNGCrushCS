using System;
using NUnit.Framework;
using FileFormat.CharSet64;

namespace FileFormat.CharSet64.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new CharSet64File {
      CharData = new byte[CharSet64File.ExpectedFileSize],
    };
    var bytes = CharSet64Writer.ToBytes(original);
    var roundTripped = CharSet64Reader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.CharData, Is.EqualTo(original.CharData));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => CharSet64Reader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => CharSet64Writer.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
