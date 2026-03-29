using System;
using NUnit.Framework;
using FileFormat.AtariPlayer;

namespace FileFormat.AtariPlayer.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new AtariPlayerFile {
      PlayerData = new byte[1024],
    };
    var bytes = AtariPlayerWriter.ToBytes(original);
    var roundTripped = AtariPlayerReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PlayerData, Is.EqualTo(original.PlayerData));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => AtariPlayerReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => AtariPlayerWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
