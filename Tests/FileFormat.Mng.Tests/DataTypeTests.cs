using System;
using FileFormat.Mng;

namespace FileFormat.Mng.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void MngTermAction_HasExpectedValues() {
    Assert.That((int)MngTermAction.ShowLast, Is.EqualTo(0));
    Assert.That((int)MngTermAction.ShowFirst, Is.EqualTo(1));
    Assert.That((int)MngTermAction.ShowBlank, Is.EqualTo(2));

    var values = Enum.GetValues<MngTermAction>();
    Assert.That(values, Has.Length.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void MngFile_DefaultFrames_IsEmpty() {
    var file = new MngFile();
    Assert.That(file.Frames, Is.Not.Null);
    Assert.That(file.Frames, Has.Count.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void MngHeader_RecordEquality() {
    var a = new MngHeader(100, 200, 1000, 1, 5, 50, 1);
    var b = new MngHeader(100, 200, 1000, 1, 5, 50, 1);
    Assert.That(a, Is.EqualTo(b));
  }
}
