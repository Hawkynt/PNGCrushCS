using System;
using System.IO;
using System.Linq;
using FileFormat.JpegXl;
using FileFormat.JpegXl.Codec;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// The scan order a frame states for itself, and the bucket that decides which
/// shapes share one.
/// </summary>
/// <remarks>
/// A frame may state, for a group of shapes, an order of its own instead of the
/// one the shape implies. It states it as a permutation of the natural order
/// rather than as positions, so the two are composed. This decoder used to read
/// the permutation only to keep its place in the bitstream and then throw it
/// away, placing every coefficient by the natural order — which puts them
/// somewhere other than the file put them, for every block of every shape the
/// frame gave an order to.
/// </remarks>
[TestFixture]
internal sealed class JxlStatedScanOrderTests {

  /// <summary>
  /// A 64x64 lossy file cjxl 0.12.0 wrote which states orders of its own.
  /// Reading them and applying them takes it from 212 pixels differing against
  /// `djxl` to 51 of its 4,096.
  /// </summary>
  [Test]
  public void AFileStatingItsOwnScanOrdersDecodes() {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "cjxl_stated_scan_order.jxl");
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");

    // The permutations are read under the format's own end-of-stream check, so
    // getting through at all means they were read exactly as written.
    var decoded = JpegXlReader.TryReadSpecRgb24(File.ReadAllBytes(path), out var width, out var height, out var rgb);

    Assert.Multiple(() => {
      Assert.That(decoded, Is.True);
      Assert.That(width, Is.EqualTo(64));
      Assert.That(height, Is.EqualTo(64));
      Assert.That(rgb, Is.Not.Null.And.Length.EqualTo(64 * 64 * 3));
    });
  }

  /// <summary>
  /// Orders are stated per bucket, and a shape shares its bucket with its own
  /// transpose — so both use whichever of the two comes first, rather than each
  /// computing its own.
  /// </summary>
  [Test]
  public void AShapeAndItsTransposeShareOneOrder() {
    var orders = new int[JxlCoeffOrderDecoder.NumOrders][][];
    for (var bucket = 0; bucket < orders.Length; ++bucket) {
      orders[bucket] = new int[3][];
      for (var channel = 0; channel < 3; ++channel)
        orders[bucket][channel] = [bucket, channel];
    }

    var wide = JxlCoeffOrderDecoder.For(orders, JxlAcStrategyType.Dct16x8, channel: 1);
    var tall = JxlCoeffOrderDecoder.For(orders, JxlAcStrategyType.Dct8x16, channel: 1);
    var other = JxlCoeffOrderDecoder.For(orders, JxlAcStrategyType.Dct16x16, channel: 1);

    Assert.Multiple(() => {
      Assert.That(wide, Is.SameAs(tall), "a shape and its transpose are one bucket");
      Assert.That(other, Is.Not.SameAs(wide), "a different shape is a different one");
      Assert.That(JxlCoeffOrderDecoder.For(orders, JxlAcStrategyType.Dct16x8, channel: 2),
        Is.Not.SameAs(wide), "each channel states its own");
    });
  }

  /// <summary>A frame that states no orders of its own leaves every bucket on
  /// the order its shape implies, and reads nothing for them.</summary>
  [Test]
  public void StatingNoOrdersLeavesEveryShapeOnItsOwnAndCostsNothing() {
    var reader = new JxlBitReader(new byte[16], 0);

    var orders = JxlCoeffOrderDecoder.DecodeCoeffOrders(reader, usedOrders: 0);

    Assert.Multiple(() => {
      Assert.That(reader.BitsRead, Is.Zero, "no histograms are stated when nothing uses them");
      Assert.That(JxlCoeffOrderDecoder.For(orders, JxlAcStrategyType.Dct8x8, channel: 0),
        Is.EqualTo(JxlNaturalCoeffOrder.For(JxlAcStrategyType.Dct8x8)));
      Assert.That(JxlCoeffOrderDecoder.For(orders, JxlAcStrategyType.Dct32x32, channel: 2),
        Is.EqualTo(JxlNaturalCoeffOrder.For(JxlAcStrategyType.Dct32x32)));
    });
  }
}
