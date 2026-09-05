using System.IO;

namespace FileFormat.Codecs.Indeo.Tests;

/// <summary>
/// The codebook generator, which turns a row-width descriptor into codes.
/// </summary>
/// <remarks>
/// Indeo never transmits code lengths. Everything a stream says about a codebook is a row count and
/// one nibble per row, and what those stand for — <i>i</i> ones, then a zero unless it is the last
/// row, then that row's nibble of index bits — is a rule with no redundancy in it: a codebook built
/// one bit differently still decodes, into different symbols.
/// </remarks>
[TestFixture]
public sealed class IviHuffmanTableTests {

  private static int _Read(IviHuffmanTable table, params int[] bits) {
    var packed = 0;
    for (var i = 0; i < bits.Length; ++i)
      packed |= bits[i] << i;

    return table.Read(new IviBitReader(new[] { (byte)packed, (byte)(packed >> 8) }));
  }

  [Test]
  [Category("Unit")]
  public void TwoRowsOfNoIndexBitsAreOneBitEach() {
    // Row zero is a single zero bit and row one, being last, is a single one bit.
    var table = IviHuffmanTable.FromDescriptor([0, 0]);

    Assert.That(_Read(table, 0), Is.Zero);
    Assert.That(_Read(table, 1), Is.EqualTo(1));
  }

  [Test]
  [Category("Unit")]
  public void ARowsIndexBitsFollowItsUnaryPrefixMostSignificantFirst() {
    // Row zero: one index bit behind a "0" terminator, so 0,0 and 0,1 are symbols 0 and 1. Row one is
    // last, so its prefix is a single "1" with no terminator and its two index bits follow.
    var table = IviHuffmanTable.FromDescriptor([1, 2]);

    Assert.That(_Read(table, 0, 0), Is.Zero);
    Assert.That(_Read(table, 0, 1), Is.EqualTo(1));
    Assert.That(_Read(table, 1, 0, 0), Is.EqualTo(2));
    Assert.That(_Read(table, 1, 0, 1), Is.EqualTo(3));
    Assert.That(_Read(table, 1, 1, 0), Is.EqualTo(4));
    Assert.That(_Read(table, 1, 1, 1), Is.EqualTo(5));
  }

  [Test]
  [Category("Unit")]
  public void ARowOfNoIndexBitsStillCostsOneBit() {
    // A descriptor of one row with no index bits describes a code of no length at all. It is stored
    // as a one-bit code so that reading it makes progress, which is what the format's own decoders do.
    var table = IviHuffmanTable.FromDescriptor([0]);
    Assert.That(_Read(table, 0), Is.Zero);
  }

  [Test]
  [Category("Unit")]
  public void ABitPatternNoRowCoversIsRefused() {
    // The rows of a descriptor need not fill the code space. A pattern outside them means the read has
    // gone out of step, not that a spare code was used, so it throws rather than resolving to zero.
    var table = IviHuffmanTable.FromDescriptor([0]);

    var failure = Assert.Throws<InvalidDataException>(() => _Read(table, 1));
    Assert.That(failure!.Message, Does.Contain("does not define"));
  }

  [Test]
  [Category("Unit")]
  public void ADescriptorNeedingMoreThanThirteenBitsIsRefused() {
    // Fourteen rows whose last carries an index bit would need a fourteen-bit code, and Indeo's are
    // thirteen at the most.
    var descriptor = new byte[14];
    descriptor[13] = 1;

    var failure = Assert.Throws<InvalidDataException>(() => IviHuffmanTable.FromDescriptor(descriptor));
    Assert.That(failure!.Message, Does.Contain("14 bits"));
  }

  [Test]
  [Category("Unit")]
  public void ADescriptorWithNoRowsIsRefused() {
    var failure = Assert.Throws<InvalidDataException>(() => IviHuffmanTable.FromDescriptor([]));
    Assert.That(failure!.Message, Does.Contain("no rows"));
  }

  [Test]
  [Category("Unit")]
  public void ADescriptorAskingForMoreThanTwoHundredAndFiftySixCodesStopsThere() {
    // A single row of nine index bits describes 512 codes. Only 256 are kept, because a block symbol
    // is an index into a 256-entry run-value map and there is nothing for the rest to mean.
    var table = IviHuffmanTable.FromDescriptor([9]);

    Assert.That(_Read(table, 0, 0, 0, 0, 0, 0, 0, 0, 0), Is.Zero);
    Assert.That(_Read(table, 0, 1, 1, 1, 1, 1, 1, 1, 1), Is.EqualTo(255));
    Assert.Throws<InvalidDataException>(() => _Read(table, 1, 0, 0, 0, 0, 0, 0, 0, 0));
  }

  [Test]
  [Category("Unit")]
  public void EveryBuiltInDescriptorBuilds() {
    foreach (var descriptor in IviTables.MacroblockDescriptors)
      Assert.That(IviHuffmanTable.FromDescriptor(descriptor), Is.Not.Null);

    foreach (var descriptor in IviTables.BlockDescriptors)
      Assert.That(IviHuffmanTable.FromDescriptor(descriptor), Is.Not.Null);
  }
}
