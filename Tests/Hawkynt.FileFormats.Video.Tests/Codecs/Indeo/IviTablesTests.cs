using System.Linq;

namespace FileFormat.Codecs.Indeo.Tests;

/// <summary>
/// The fixed tables, checked for the properties that make them tables of this format rather than
/// plausible tables.
/// </summary>
/// <remarks>
/// These are the values a stream selects by index and never carries, so nothing in a decode reveals
/// one that is wrong: a scan pattern off by one entry puts a handful of coefficients in the wrong
/// place, which looks like a slightly noisy picture. What can be checked cheaply is that each scan is
/// a permutation — every position of the block reached exactly once — and that the run-value maps and
/// codebook descriptors are the right shape.
/// </remarks>
[TestFixture]
public sealed class IviTablesTests {

  private static readonly (string Name, byte[] Scan, int Size)[] _Scans = [
    (nameof(IviTables.ZigzagDirect), IviTables.ZigzagDirect, 64),
    (nameof(IviTables.VerticalScan8x8), IviTables.VerticalScan8x8, 64),
    (nameof(IviTables.HorizontalScan8x8), IviTables.HorizontalScan8x8, 64),
    (nameof(IviTables.DirectScan4x4), IviTables.DirectScan4x4, 16),
    (nameof(Indeo4Tables.AlternateScan8x8), Indeo4Tables.AlternateScan8x8, 64),
    (nameof(Indeo4Tables.AlternateScan4x4), Indeo4Tables.AlternateScan4x4, 16),
    (nameof(Indeo4Tables.VerticalScan4x4), Indeo4Tables.VerticalScan4x4, 16),
    (nameof(Indeo4Tables.HorizontalScan4x4), Indeo4Tables.HorizontalScan4x4, 16),
  ];

  [Test]
  [Category("Unit")]
  public void EveryScanReachesEveryPositionOfItsBlockExactlyOnce() {
    foreach (var (name, scan, size) in _Scans) {
      Assert.That(scan, Has.Length.EqualTo(size), name);
      Assert.That(
        scan.Select(x => (int)x).OrderBy(x => x),
        Is.EqualTo(Enumerable.Range(0, size)).AsCollection,
        name);
    }
  }

  [Test]
  [Category("Unit")]
  public void TheZigzagIsJpegsAndNotAVariantOfIt()
    => Assert.That(IviTables.ZigzagDirect[..8], Is.EqualTo(new byte[] { 0, 1, 8, 16, 9, 2, 3, 10 }).AsCollection);

  [Test]
  [Category("Unit")]
  public void ThereAreEightCodebookDescriptorsOfEachKind() {
    Assert.That(IviTables.MacroblockDescriptors, Has.Length.EqualTo(8));
    Assert.That(IviTables.BlockDescriptors, Has.Length.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void EveryRunValueMapCoversAllTwoHundredAndFiftySixSymbols() {
    Assert.That(IviRunValueMap.Defaults, Has.Length.EqualTo(9));

    foreach (var map in IviRunValueMap.Defaults) {
      Assert.That(map.Runs, Has.Length.EqualTo(256));
      Assert.That(map.Values, Has.Length.EqualTo(256));
      Assert.That(map.EndOfBlockSymbol, Is.InRange(0, 255));
      Assert.That(map.EscapeSymbol, Is.InRange(0, 255));
      Assert.That(map.EndOfBlockSymbol, Is.Not.EqualTo(map.EscapeSymbol));
    }
  }

  [Test]
  [Category("Unit")]
  public void TheDequantisationMatricesAreTheShapeTheirBlocksAre() {
    Assert.That(Indeo4Tables.Quant8x8Intra, Has.Length.EqualTo(9));
    Assert.That(Indeo4Tables.Quant8x8Inter, Has.Length.EqualTo(9));
    Assert.That(Indeo4Tables.Quant4x4Intra, Has.Length.EqualTo(5));
    Assert.That(Indeo4Tables.Quant4x4Inter, Has.Length.EqualTo(5));
    Assert.That(Indeo4Tables.Quant8x8Intra.All(m => m.Length == 64));
    Assert.That(Indeo4Tables.Quant4x4Intra.All(m => m.Length == 16));

    Assert.That(Indeo5Tables.BaseQuant8x8Intra, Has.Length.EqualTo(5));
    Assert.That(Indeo5Tables.BaseQuant8x8Inter, Has.Length.EqualTo(5));
    Assert.That(Indeo5Tables.BaseQuant4x4Intra, Has.Length.EqualTo(16));
    Assert.That(Indeo5Tables.ScaleQuant8x8Intra.All(m => m.Length == 24));
    Assert.That(Indeo5Tables.ScaleQuant4x4Intra, Has.Length.EqualTo(24));
  }

  [Test]
  [Category("Unit")]
  public void EveryQuantiserMatrixIndexIndeo4DefinesNamesAMatrixThatExists() {
    Assert.That(Indeo4Tables.QuantIndexToTable, Has.Length.EqualTo(22));

    foreach (var index in Indeo4Tables.QuantIndexToTable)
      Assert.That(index, Is.LessThan(Indeo4Tables.Quant8x8Intra.Length));
  }
}
