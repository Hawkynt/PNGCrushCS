using System;
using FileFormat.Ccitt;

namespace FileFormat.Ccitt.Tests;

/// <summary>
/// The make-up codes for runs of 1792 and longer, which both colours share.
/// </summary>
/// <remarks>
/// They were missing altogether. A colour's own make-up codes stop at 1728, so any run longer than
/// 1791 had no code that matched, the decoder gave up on the line, and the outer loop stopped —
/// leaving every remaining row blank while reporting no trouble at all.
/// <para/>
/// On a page any wider than about 1800 pixels that happens almost at once. A 4824 by 7231 CALS
/// raster came back entirely black; with these codes present it matches ImageMagick on all 34882344
/// of its pixels.
/// <para/>
/// Unlike the per-colour tables these carry their run length rather than implying it from position,
/// the steps not being a plain multiple of the index.
/// </remarks>
[TestFixture]
public sealed class CcittLongRunTests {

  [Test]
  [Category("Unit")]
  public void TheTableCoversEveryLengthTheStandardNames() {
    int[] expected = [1792, 1856, 1920, 1984, 2048, 2112, 2176, 2240, 2304, 2368, 2432, 2496, 2560];

    Assert.That(CcittHuffmanTable.SharedMakeUp.Length, Is.EqualTo(expected.Length));
    Assert.Multiple(() => {
      for (var i = 0; i < expected.Length; ++i)
        Assert.That(CcittHuffmanTable.SharedMakeUp[i].RunLength, Is.EqualTo(expected[i]), $"entry {i}");
    });
  }

  [Test]
  [Category("Unit")]
  public void TheLengthsRiseInStepsOfSixtyFour() {
    for (var i = 1; i < CcittHuffmanTable.SharedMakeUp.Length; ++i)
      Assert.That(
        CcittHuffmanTable.SharedMakeUp[i].RunLength - CcittHuffmanTable.SharedMakeUp[i - 1].RunLength,
        Is.EqualTo(64), $"between entries {i - 1} and {i}");
  }

  [Test]
  [Category("Unit")]
  public void EveryCodeFitsTheLengthItStates() {
    Assert.Multiple(() => {
      foreach (var (code, bits, run) in CcittHuffmanTable.SharedMakeUp) {
        Assert.That(bits, Is.InRange(11, 13), $"run {run}");
        Assert.That(code, Is.LessThan(1 << bits), $"run {run} states more bits than it uses");
      }
    });
  }

  [Test]
  [Category("Unit")]
  public void NoCodeIsThePrefixOfAnother() {
    // A prefix-free set is what makes the stream readable without a length in front of each code.
    var all = CcittHuffmanTable.SharedMakeUp;

    Assert.Multiple(() => {
      for (var i = 0; i < all.Length; ++i)
      for (var j = 0; j < all.Length; ++j) {
        if (i == j || all[i].BitLength > all[j].BitLength)
          continue;

        var shifted = all[j].Code >> (all[j].BitLength - all[i].BitLength);
        Assert.That(shifted, Is.Not.EqualTo(all[i].Code),
          $"run {all[i].RunLength} is a prefix of run {all[j].RunLength}");
      }
    });
  }

  [Test]
  [Category("Unit")]
  public void TheyDoNotCollideWithAColoursOwnMakeUpCodes() {
    Assert.Multiple(() => {
      foreach (var (code, bits, run) in CcittHuffmanTable.SharedMakeUp)
      foreach (var table in new[] { CcittHuffmanTable.WhiteMakeUp, CcittHuffmanTable.BlackMakeUp })
      foreach (var (otherCode, otherBits) in table)
        if (otherBits == bits)
          Assert.That(otherCode, Is.Not.EqualTo(code), $"run {run} collides with a per-colour code");
    });
  }
}
