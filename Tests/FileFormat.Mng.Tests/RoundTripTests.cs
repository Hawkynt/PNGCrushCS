using System;
using System.IO;
using FileFormat.Mng;

namespace FileFormat.Mng.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SingleFrame() {
    var png = MngTestHelper.BuildMinimalPng();
    var original = new MngFile {
      Width = 1,
      Height = 1,
      TicksPerSecond = 1000,
      NumPlays = 0,
      TermAction = MngTermAction.ShowLast,
      Frames = [png]
    };

    var bytes = MngWriter.ToBytes(original);
    var restored = MngReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
    Assert.That(restored.TicksPerSecond, Is.EqualTo(original.TicksPerSecond));
    Assert.That(restored.Frames, Has.Count.EqualTo(1));
    Assert.That(restored.Frames[0], Is.EqualTo(original.Frames[0]));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultiFrame() {
    var png1 = MngTestHelper.BuildMinimalPng();
    var png2 = MngTestHelper.BuildMinimalPng();
    var original = new MngFile {
      Width = 1,
      Height = 1,
      TicksPerSecond = 500,
      NumPlays = 3,
      TermAction = MngTermAction.ShowFirst,
      Frames = [png1, png2]
    };

    var bytes = MngWriter.ToBytes(original);
    var restored = MngReader.FromBytes(bytes);

    Assert.That(restored.Frames, Has.Count.EqualTo(2));
    Assert.That(restored.Frames[0], Is.EqualTo(original.Frames[0]));
    Assert.That(restored.Frames[1], Is.EqualTo(original.Frames[1]));
    Assert.That(restored.TermAction, Is.EqualTo(MngTermAction.ShowFirst));
    Assert.That(restored.NumPlays, Is.EqualTo(3));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile() {
    var png = MngTestHelper.BuildMinimalPng();
    var original = new MngFile {
      Width = 1,
      Height = 1,
      TicksPerSecond = 1000,
      Frames = [png]
    };

    var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mng");
    try {
      var bytes = MngWriter.ToBytes(original);
      File.WriteAllBytes(tempPath, bytes);
      var restored = MngReader.FromFile(new FileInfo(tempPath));

      Assert.That(restored.Width, Is.EqualTo(original.Width));
      Assert.That(restored.Height, Is.EqualTo(original.Height));
      Assert.That(restored.Frames, Has.Count.EqualTo(1));
    } finally {
      if (File.Exists(tempPath))
        File.Delete(tempPath);
    }
  }
}
