using System;
using FileFormat.Core.BlockDecoders;

namespace FileFormat.Core.BlockDecoders.Tests;

/// <summary>
/// Telling an ETC1-compatible block apart from the three arrangements ETC2 adds.
/// </summary>
/// <remarks>
/// Every block used to be handed to the ETC1 decoder, whatever it was. ETC2 reuses the bit patterns
/// ETC1 could not produce: in differential mode each channel holds a five-bit base and a three-bit
/// signed delta, and ETC1 never lets the sum leave the five-bit range, so ETC2 gives each overflow a
/// meaning of its own — red for the T arrangement, green for the H one, blue for the planar one.
/// None of the three resembles what ETC1 makes of the same bits, so a real ETC2 texture came out as
/// blocks of unrelated colours with nothing reporting a problem.
/// <para/>
/// The test is arithmetic rather than a guess, which is why it can be checked here without a
/// reference decoder: these blocks are built with the overflow put in on purpose.
/// </remarks>
[TestFixture]
public sealed class Etc2ModeDetectionTests {

  /// <summary>Builds a differential-mode block whose channels carry the given base and delta.</summary>
  private static byte[] _Differential((int Base, int Delta) r, (int Base, int Delta) g, (int Base, int Delta) b) {
    var block = new byte[8];
    block[0] = (byte)((r.Base << 3) | (r.Delta & 7));
    block[1] = (byte)((g.Base << 3) | (g.Delta & 7));
    block[2] = (byte)((b.Base << 3) | (b.Delta & 7));
    block[3] = 2; // the bit that selects differential mode
    return block;
  }

  [Test]
  [Category("Unit")]
  public void ABlockWhoseChannelsStayInRangeIsDecoded() {
    var output = new byte[64];

    Assert.That(Etc2Decoder.DecodeEtc2RgbBlock(_Differential((15, 1), (15, -1), (15, 2)), output), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void RedRunningPastTheTopIsTheArrangementCalledT() {
    var output = new byte[64];

    // 31 + 1 leaves the five bits, which ETC1 cannot express and ETC2 uses for something else.
    Assert.That(Etc2Decoder.DecodeEtc2RgbBlock(_Differential((31, 1), (15, 0), (15, 0)), output), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void GreenRunningPastTheBottomIsTheArrangementCalledH() {
    var output = new byte[64];

    Assert.That(Etc2Decoder.DecodeEtc2RgbBlock(_Differential((15, 0), (0, -1), (15, 0)), output), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void BlueRunningPastTheTopIsThePlanarArrangement() {
    var output = new byte[64];

    Assert.That(Etc2Decoder.DecodeEtc2RgbBlock(_Differential((15, 0), (15, 0), (31, 3)), output), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void IndividualModeIsPlainEtc1AndNeverOneOfTheThree() {
    // Without the differential bit the bytes are two four-bit colours, and no sum is taken at all.
    var block = new byte[8];
    block[0] = 0xFF;
    block[1] = 0xFF;
    block[2] = 0xFF;
    block[3] = 0;

    var output = new byte[64];

    Assert.That(Etc2Decoder.DecodeEtc2RgbBlock(block, output), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ABlockItCannotReadIsLeftBlankRatherThanFilledWithSomethingElse() {
    var output = new byte[64];
    Array.Fill(output, (byte)0x7F);
    Etc2Decoder.DecodeEtc2RgbBlock(_Differential((31, 1), (15, 0), (15, 0)), output);

    foreach (var value in output)
      Assert.That(value, Is.Zero);
  }

  [Test]
  [Category("Unit")]
  public void AnImageCountsTheBlocksItCouldNotRead() {
    // Two blocks side by side, the second using an arrangement this does not decode.
    var data = new byte[16];
    _Differential((15, 1), (15, 1), (15, 1)).CopyTo(data, 0);
    _Differential((31, 1), (15, 0), (15, 0)).CopyTo(data, 8);

    var output = new byte[8 * 4 * 4];

    Assert.That(Etc2Decoder.DecodeEtc2RgbImage(data, 8, 4, output), Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void AnImageOfBlocksItCanReadReportsNothingLeft() {
    var data = new byte[16];
    _Differential((15, 1), (15, 1), (15, 1)).CopyTo(data, 0);
    _Differential((10, -2), (12, 3), (8, 1)).CopyTo(data, 8);

    var output = new byte[8 * 4 * 4];

    Assert.That(Etc2Decoder.DecodeEtc2RgbImage(data, 8, 4, output), Is.Zero);
  }
}
