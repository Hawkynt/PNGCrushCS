using System;
using NUnit.Framework;
using FileFormat.HiResEditor;

namespace FileFormat.HiResEditor.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new HiResEditorFile {
      LoadAddress = 0x2000,
      BitmapData = new byte[8000],
      ScreenData = new byte[1000],
    };
    var bytes = HiResEditorWriter.ToBytes(original);
    var roundTripped = HiResEditorReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(roundTripped.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(roundTripped.ScreenData, Is.EqualTo(original.ScreenData));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => HiResEditorReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => HiResEditorWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
