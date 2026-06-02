using System;
using System.IO;
using FileFormat.GraspGl;

namespace FileFormat.GraspGl.Tests;

[TestFixture]
public sealed class GraspGlTests {

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => GraspGlReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_ThrowsFileNotFoundException() {
    var missing = new FileInfo(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gl"));
    Assert.Throws<FileNotFoundException>(() => GraspGlReader.FromFile(missing));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => GraspGlReader.FromBytes(new byte[1]));

  [Test]
  [Category("Unit")]
  public void Writer_RoundTrip_SingleEntry() {
    var original = new GraspGlFile {
      Entries = [new GraspGlFile.GraspEntry("FRAME1.CLP", [0x10, 0x20, 0x30, 0x40])],
    };
    var bytes = GraspGlWriter.ToBytes(original);
    var loaded = GraspGlReader.FromSpan(bytes);
    Assert.That(loaded.Entries, Has.Length.EqualTo(1));
    Assert.That(loaded.Entries[0].Name, Is.EqualTo("FRAME1.CLP"));
    Assert.That(loaded.Entries[0].Data, Is.EqualTo(original.Entries[0].Data));
  }

  [Test]
  [Category("Unit")]
  public void Writer_RejectsOverlongName() {
    var bad = new GraspGlFile {
      Entries = [new GraspGlFile.GraspEntry("THIRTEENCHAR1", [0])],
    };
    Assert.Throws<InvalidDataException>(() => GraspGlWriter.ToBytes(bad));
  }

  [Test]
  [Category("Unit")]
  public void Writer_RoundTrip_MultipleEntries() {
    var original = new GraspGlFile {
      Entries = [
        new("A.CLP", [1, 2, 3]),
        new("B.PIC", [4, 5]),
      ],
    };
    var bytes = GraspGlWriter.ToBytes(original);
    var loaded = GraspGlReader.FromSpan(bytes);
    Assert.That(loaded.Entries, Has.Length.EqualTo(2));
    Assert.That(loaded.Entries[0].Name, Is.EqualTo("A.CLP"));
    Assert.That(loaded.Entries[1].Name, Is.EqualTo("B.PIC"));
  }
}
