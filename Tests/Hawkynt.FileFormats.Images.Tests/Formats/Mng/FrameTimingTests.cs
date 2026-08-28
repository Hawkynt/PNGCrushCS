using FileFormat.Core;
using FileFormat.Png;

namespace FileFormat.Mng.Tests;

[TestFixture]
public sealed class FrameTimingTests {

  private static byte[] _Frame(byte value) {
    var image = new RawImage {
      Width = 2,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [value, 1, 2, value, 3, 4],
    };
    return PngWriter.ToBytes(PngFile.FromRawImage(image));
  }

  [Test]
  [Category("Integration")]
  public void VariableDelays_RoundTripExactly() {
    var file = new MngFile {
      Width = 2,
      Height = 1,
      TicksPerSecond = 1000,
      Frames = [_Frame(10), _Frame(20), _Frame(30)],
      FrameDelays = [16, 33, 250],
      TermAction = MngTermAction.ShowLast,
    };

    var restored = MngReader.FromBytes(MngWriter.ToBytes(file));

    Assert.That(restored.Frames, Has.Count.EqualTo(3));
    Assert.That(restored.FrameDelays, Is.EqualTo(new[] { 16, 33, 250 }));
    Assert.That(restored.TicksPerSecond, Is.EqualTo(1000));
  }

  [Test]
  [Category("Unit")]
  public void VariableDelays_EnableSimpleMNGProfileBitAndNominalPlayTime() {
    var bytes = MngWriter.ToBytes(new MngFile {
      Width = 2,
      Height = 1,
      TicksPerSecond = 100,
      Frames = [_Frame(1), _Frame(2)],
      FrameDelays = [7, 11],
      TermAction = MngTermAction.ShowLast,
    });

    // MNG signature (8), MHDR length/type (8), then MHDR data. nominal_play_time is bytes 20..23
    // within MHDR; simplicity_profile is bytes 24..27.
    var mhdrData = bytes.AsSpan(16, 28);
    var playTime = (uint)(mhdrData[20] << 24 | mhdrData[21] << 16 | mhdrData[22] << 8 | mhdrData[23]);
    var profile = (uint)(mhdrData[24] << 24 | mhdrData[25] << 16 | mhdrData[26] << 8 | mhdrData[27]);

    Assert.That(playTime, Is.EqualTo(18));
    Assert.That((profile & 0x2u), Is.EqualTo(0x2u), "FRAM requires the simple-MNG-features profile bit");
  }

  [Test]
  [Category("Unit")]
  public void ExplicitAnimationTiming_RequiresTickClock() {
    var file = new MngFile {
      Width = 2,
      Height = 1,
      TicksPerSecond = 0,
      Frames = [_Frame(1), _Frame(2)],
      FrameDelays = [1, 1],
    };

    Assert.That(() => MngWriter.ToBytes(file), Throws.TypeOf<System.IO.InvalidDataException>());
  }

  [Test]
  [Category("Integration")]
  public void NoFram_UsesSpecificationDefaultOneTick() {
    var restored = MngReader.FromBytes(MngWriter.ToBytes(new MngFile {
      Width = 2,
      Height = 1,
      TicksPerSecond = 60,
      Frames = [_Frame(1), _Frame(2)],
      FrameDelays = [],
    }));

    Assert.That(restored.FrameDelays, Is.EqualTo(new[] { 1, 1 }));
  }
}
