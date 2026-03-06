using FileFormat.Ani;

namespace FileFormat.Ani.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void AniHeader_RecordStruct_StoresFields() {
    var header = new AniHeader(
      CbSize: 36,
      NumFrames: 3,
      NumSteps: 5,
      Width: 32,
      Height: 32,
      BitCount: 24,
      NumPlanes: 1,
      DisplayRate: 10,
      Flags: 3
    );

    Assert.That(header.CbSize, Is.EqualTo(36));
    Assert.That(header.NumFrames, Is.EqualTo(3));
    Assert.That(header.NumSteps, Is.EqualTo(5));
    Assert.That(header.Width, Is.EqualTo(32));
    Assert.That(header.Height, Is.EqualTo(32));
    Assert.That(header.BitCount, Is.EqualTo(24));
    Assert.That(header.NumPlanes, Is.EqualTo(1));
    Assert.That(header.DisplayRate, Is.EqualTo(10));
    Assert.That(header.Flags, Is.EqualTo(3));
    Assert.That(header.HasSequence, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void AniHeader_DefaultValues() {
    var header = new AniHeader();

    Assert.That(header.CbSize, Is.EqualTo(0));
    Assert.That(header.NumFrames, Is.EqualTo(0));
    Assert.That(header.NumSteps, Is.EqualTo(0));
    Assert.That(header.Width, Is.EqualTo(0));
    Assert.That(header.Height, Is.EqualTo(0));
    Assert.That(header.BitCount, Is.EqualTo(0));
    Assert.That(header.NumPlanes, Is.EqualTo(0));
    Assert.That(header.DisplayRate, Is.EqualTo(0));
    Assert.That(header.Flags, Is.EqualTo(0));
    Assert.That(header.HasSequence, Is.False);
  }
}
