using System;
using NUnit.Framework;
using FileFormat.PictureEditor;

namespace FileFormat.PictureEditor.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void WriteThenRead_PreservesData() {
    var original = new PictureEditorFile {
      PixelData = new byte[PictureEditorFile.ExpectedFileSize],
    };
    var bytes = PictureEditorWriter.ToBytes(original);
    var roundTripped = PictureEditorReader.FromBytes(bytes);
    Assert.That(roundTripped, Is.Not.Null);
    Assert.That(roundTripped.PixelData, Is.EqualTo(original.PixelData));
  }

  [Test]
  public void Reader_NullBytes_ThrowsArgumentNullException() {
    Assert.That(() => PictureEditorReader.FromBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }

  [Test]
  public void Writer_NullFile_ThrowsArgumentNullException() {
    Assert.That(() => PictureEditorWriter.ToBytes(null!), Throws.TypeOf<ArgumentNullException>());
  }
}
