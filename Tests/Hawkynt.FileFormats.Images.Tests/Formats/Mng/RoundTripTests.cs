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
      // An iteration count only exists in TERM's ten-byte repeat form. Asking for one alongside
      // ShowFirst asks the format to carry a field that form has no room for, so the repeat action
      // is what this exercises, with ShowFirst as what happens once the iterations are done.
      NumPlays = 3,
      TermAction = MngTermAction.Repeat,
      ActionAfterIterations = MngTermAction.ShowFirst,
      Frames = [png1, png2]
    };

    var bytes = MngWriter.ToBytes(original);
    var restored = MngReader.FromBytes(bytes);

    Assert.That(restored.Frames, Has.Count.EqualTo(2));
    Assert.That(restored.Frames[0], Is.EqualTo(original.Frames[0]));
    Assert.That(restored.Frames[1], Is.EqualTo(original.Frames[1]));
    Assert.That(restored.TermAction, Is.EqualTo(MngTermAction.Repeat));
    Assert.That(restored.ActionAfterIterations, Is.EqualTo(MngTermAction.ShowFirst));
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
