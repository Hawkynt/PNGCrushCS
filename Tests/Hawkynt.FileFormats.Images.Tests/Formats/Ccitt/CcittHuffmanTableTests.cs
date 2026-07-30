using System;
using FileFormat.Ccitt;

namespace FileFormat.Ccitt.Tests;

[TestFixture]
public sealed class CcittHuffmanTableTests {

  [Test]
  [Category("Unit")]
  public void WhiteTerminating_Has64Entries() {
    Assert.That(CcittHuffmanTable.WhiteTerminating, Has.Length.EqualTo(64));
  }

  [Test]
  [Category("Unit")]
  public void BlackTerminating_Has64Entries() {
    Assert.That(CcittHuffmanTable.BlackTerminating, Has.Length.EqualTo(64));
  }

  [Test]
  [Category("Unit")]
  public void WhiteMakeUp_Has27Entries() {
    // Run lengths 64, 128, ..., 1728 = 27 entries
    Assert.That(CcittHuffmanTable.WhiteMakeUp, Has.Length.EqualTo(27));
  }

  [Test]
  [Category("Unit")]
  public void BlackMakeUp_Has27Entries() {
    Assert.That(CcittHuffmanTable.BlackMakeUp, Has.Length.EqualTo(27));
  }

  [Test]
  [Category("Unit")]
  public void WhiteTerminating_AllHavePositiveBitLengths() {
    foreach (var (_, bitLength) in CcittHuffmanTable.WhiteTerminating)
      Assert.That(bitLength, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void BlackTerminating_AllHavePositiveBitLengths() {
    foreach (var (_, bitLength) in CcittHuffmanTable.BlackTerminating)
      Assert.That(bitLength, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void WhiteMakeUp_AllHavePositiveBitLengths() {
    foreach (var (_, bitLength) in CcittHuffmanTable.WhiteMakeUp)
      Assert.That(bitLength, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void BlackMakeUp_AllHavePositiveBitLengths() {
    foreach (var (_, bitLength) in CcittHuffmanTable.BlackMakeUp)
      Assert.That(bitLength, Is.GreaterThan(0));
  }

  [Test]
  [Category("Unit")]
  public void EolCode_Is12Bits() {
    Assert.That(CcittHuffmanTable.EolBitLength, Is.EqualTo(12));
    Assert.That(CcittHuffmanTable.EolCode, Is.EqualTo(1)); // 000000000001
  }

  [Test]
  [Category("Unit")]
  public void WhiteTerminating_CodesAreUnique() {
    var seen = new System.Collections.Generic.HashSet<(int, int)>();
    foreach (var entry in CcittHuffmanTable.WhiteTerminating)
      Assert.That(seen.Add(entry), Is.True, $"Duplicate white terminating code: ({entry.Code}, {entry.BitLength})");
  }

  [Test]
  [Category("Unit")]
  public void BlackTerminating_CodesAreUnique() {
    var seen = new System.Collections.Generic.HashSet<(int, int)>();
    foreach (var entry in CcittHuffmanTable.BlackTerminating)
      Assert.That(seen.Add(entry), Is.True, $"Duplicate black terminating code: ({entry.Code}, {entry.BitLength})");
  }
}
