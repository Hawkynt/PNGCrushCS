namespace FileFormat.Codecs.Indeo.Tests;

/// <summary>
/// The run-value maps and the permutation a band header may apply to one.
/// </summary>
/// <remarks>
/// A band can name up to sixty-one pairs of symbols whose meanings swap for the length of that band,
/// and the end-of-block and escape symbols move with them — so a permutation can change which symbol
/// ends a block. The swap has to come off again when the band is done, because the nine maps are
/// shared by every band of every frame.
/// </remarks>
[TestFixture]
public sealed class IviRunValueMapTests {

  private static IviRunValueMap _Map() => new(5, 2, new byte[256], new sbyte[256]);

  [Test]
  [Category("Unit")]
  public void SwappingExchangesBothTheRunAndTheValue() {
    var map = _Map();
    map.Runs[7] = 3;
    map.Values[7] = -4;
    map.Runs[9] = 11;
    map.Values[9] = 2;

    map.Swap(7, 9);

    Assert.That(map.Runs[7], Is.EqualTo(11));
    Assert.That(map.Values[7], Is.EqualTo(2));
    Assert.That(map.Runs[9], Is.EqualTo(3));
    Assert.That(map.Values[9], Is.EqualTo(-4));
  }

  [Test]
  [Category("Unit")]
  public void TheEndOfBlockAndEscapeSymbolsFollowTheirEntries() {
    var map = _Map();

    map.Swap(5, 40);
    Assert.That(map.EndOfBlockSymbol, Is.EqualTo(40));
    Assert.That(map.EscapeSymbol, Is.EqualTo(2));

    map.Swap(60, 2);
    Assert.That(map.EscapeSymbol, Is.EqualTo(60));
    Assert.That(map.EndOfBlockSymbol, Is.EqualTo(40));
  }

  [Test]
  [Category("Unit")]
  public void ApplyingThePairsInReverseUndoesThem() {
    var map = _Map();
    for (var i = 0; i < 256; ++i) {
      map.Runs[i] = (byte)i;
      map.Values[i] = (sbyte)(i - 128);
    }

    var pairs = new[] { (5, 200), (2, 3), (17, 17), (40, 41) };

    foreach (var (first, second) in pairs)
      map.Swap(first, second);

    for (var i = pairs.Length - 1; i >= 0; --i)
      map.Swap(pairs[i].Item1, pairs[i].Item2);

    Assert.That(map.EndOfBlockSymbol, Is.EqualTo(5));
    Assert.That(map.EscapeSymbol, Is.EqualTo(2));
    for (var i = 0; i < 256; ++i) {
      Assert.That(map.Runs[i], Is.EqualTo((byte)i));
      Assert.That(map.Values[i], Is.EqualTo((sbyte)(i - 128)));
    }
  }

  [Test]
  [Category("Unit")]
  public void ACopyCanBePermutedWithoutDisturbingWhatItCameFrom() {
    var original = IviRunValueMap.Defaults[0];
    var (firstRun, secondRun) = (original.Runs[0], original.Runs[2]);
    Assert.That(firstRun, Is.Not.EqualTo(secondRun), "The two symbols this swaps have to differ for the test to say anything.");

    var copy = original.Clone();
    copy.Swap(0, 2);

    Assert.That(copy.Runs[0], Is.EqualTo(secondRun));
    Assert.That(original.Runs[0], Is.EqualTo(firstRun));
    Assert.That(original.Runs[2], Is.EqualTo(secondRun));
  }
}
