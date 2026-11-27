using System;

namespace FileFormat.LuraWave.Tests;

[TestFixture]
public sealed class LuraWaveCodecTests {

  private static void _PutWord(LuraWaveBitWriter writer, uint word) {
    for (var bit = 31; bit >= 0; --bit)
      writer.Put((word >> bit & 1) != 0);
  }

  private static uint _ReadWord(LuraWaveBitReader reader) {
    var word = 0u;
    for (var i = 0; i < 32; ++i)
      word = word << 1 | (reader.ReadBit() ? 1u : 0u);
    return word;
  }

  [Test]
  [Category("Unit")]
  public void DataWordsThatLookLikeControlWords_AreEscaped() {
    var writer = new LuraWaveBitWriter();
    uint[] words = [0x12345678, 0xFFFFFFFF, 0xFFFFFF00, 0xFFFFFF7F, 0xFFFFFFFE, 0xFFFFFEFF];
    foreach (var word in words)
      _PutWord(writer, word);
    var bytes = writer.Finish();

    // Four of the six need an escape word in front, and the stripe ends with its terminator.
    Assert.That(bytes, Has.Length.EqualTo((6 + 4 + 1) * 4));

    var reader = new LuraWaveBitReader(bytes, 0);
    foreach (var word in words)
      Assert.That(_ReadWord(reader), Is.EqualTo(word));
    Assert.That(reader.ReadBit(), Is.False);
    Assert.That(reader.State, Is.EqualTo(LuraWaveReadState.StripeEnd));
  }

  [Test]
  [Category("Unit")]
  public void FillerControlWords_CarryNoBits() {
    byte[] data = [0xFF, 0xFF, 0xFF, 0x42, 0x80, 0x00, 0x00, 0x00];
    var reader = new LuraWaveBitReader(data, 0);
    Assert.That(reader.ReadBit(), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void TheReaderStopsAtTheEndOfTheData() {
    var reader = new LuraWaveBitReader([0x80, 0x00, 0x00, 0x00], 0);
    Assert.That(reader.ReadBit(), Is.True);
    for (var i = 0; i < 31; ++i)
      reader.ReadBit();
    Assert.Multiple(() => {
      Assert.That(reader.ReadBit(), Is.False);
      Assert.That(reader.State, Is.EqualTo(LuraWaveReadState.Exhausted));
    });
  }

  [Test]
  [Category("Unit")]
  public void TheWaveletInvertsExactly() {
    var random = new Random(7);
    var plane = new int[64 * 32 * 3];
    for (var i = 0; i < plane.Length; ++i)
      plane[i] = random.Next(-40000, 40000);
    var original = (int[])plane.Clone();

    for (var channel = 0; channel < 3; ++channel)
      LuraWaveWavelet.Forward(plane, 64, 32, 3, channel, 5);
    Assert.That(plane, Is.Not.EqualTo(original));
    for (var channel = 0; channel < 3; ++channel)
      LuraWaveWavelet.Inverse(plane, 64, 32, 3, channel, 5);
    Assert.That(plane, Is.EqualTo(original));
  }

  [TestCase(1024, 10, 30)]
  [TestCase(900, 10, 20)]
  [TestCase(512, 10, 10)]
  [TestCase(0, 10, 4)]
  [Category("Unit")]
  public void PassBudget_FollowsTheQualityCurve(int quality, int topPlane, int passes)
    => Assert.That(LuraWaveStripeCoder.PassBudget(quality, topPlane), Is.EqualTo(passes));
}
