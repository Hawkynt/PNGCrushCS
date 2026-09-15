using System.Linq;

namespace FileFormat.Codecs.Dv.Tests;

/// <summary>Fixed structural invariants of the SMPTE 370M DV100 profiles.</summary>
[TestFixture]
public class Dv100ProfileTests {

  [Test]
  [Category("Unit")]
  public void EightDv100CellsExactlyFillOneVideoDifPayload() {
    Assert.Multiple(() => {
      Assert.That(DvProfile.Dv100BlockSizes, Has.Length.EqualTo(8));
      Assert.That(DvProfile.Dv100BlockSizes.Sum(), Is.EqualTo(76 * 8),
        "the cells plus the one-byte macroblock header must fill a 77-byte video DIF payload");
      Assert.That(DvProfile.Dv100BlockSizes.Take(6), Is.All.EqualTo(80));
      Assert.That(DvProfile.Dv100BlockSizes.Skip(6), Is.All.EqualTo(64));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheFourDv100ProfilesStateTheNormativeTopology() {
    Assert.Multiple(() => {
      _Profile(DvProfile.DvcproHd1080I60, 1280, 1080, 0x14, 10, 4, 480000, false);
      _Profile(DvProfile.DvcproHd1080I50, 1440, 1080, 0x14, 12, 4, 576000, false);
      _Profile(DvProfile.DvcproHd720P60, 960, 720, 0x18, 10, 2, 240000, true);
      _Profile(DvProfile.DvcproHd720P50, 960, 720, 0x18, 12, 2, 288000, true);
    });
  }

  private static void _Profile(
    DvProfile profile, int width, int height, int signalType, int sequences, int channels, int bytes, bool progressive) {
    Assert.That(profile.IsDv100, Is.True, profile.Name);
    Assert.That(profile.Width, Is.EqualTo(width), profile.Name);
    Assert.That(profile.Height, Is.EqualTo(height), profile.Name);
    Assert.That(profile.SignalType, Is.EqualTo(signalType), profile.Name);
    Assert.That(profile.SequencesPerChannel, Is.EqualTo(sequences), profile.Name);
    Assert.That(profile.ChannelCount, Is.EqualTo(channels), profile.Name);
    Assert.That(profile.FrameSize, Is.EqualTo(bytes), profile.Name);
    Assert.That(profile.Sampling, Is.EqualTo(DvSampling.FourTwoTwo), profile.Name);
    Assert.That(profile.Progressive, Is.EqualTo(progressive), profile.Name);
  }
}
