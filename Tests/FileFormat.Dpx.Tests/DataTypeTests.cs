using System;
using FileFormat.Dpx;

namespace FileFormat.Dpx.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void DpxDescriptor_HasExpectedValues() {
    Assert.That((int)DpxDescriptor.UserDefined, Is.EqualTo(0));
    Assert.That((int)DpxDescriptor.Red, Is.EqualTo(1));
    Assert.That((int)DpxDescriptor.Green, Is.EqualTo(2));
    Assert.That((int)DpxDescriptor.Blue, Is.EqualTo(3));
    Assert.That((int)DpxDescriptor.Alpha, Is.EqualTo(4));
    Assert.That((int)DpxDescriptor.Luma, Is.EqualTo(6));
    Assert.That((int)DpxDescriptor.ColorDifferenceCbCr, Is.EqualTo(7));
    Assert.That((int)DpxDescriptor.Depth, Is.EqualTo(8));
    Assert.That((int)DpxDescriptor.Composite, Is.EqualTo(9));
    Assert.That((int)DpxDescriptor.Rgb, Is.EqualTo(50));
    Assert.That((int)DpxDescriptor.Rgba, Is.EqualTo(51));
    Assert.That((int)DpxDescriptor.Abgr, Is.EqualTo(52));

    var values = Enum.GetValues<DpxDescriptor>();
    Assert.That(values, Has.Length.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void DpxPacking_HasExpectedValues() {
    Assert.That((int)DpxPacking.Packed, Is.EqualTo(0));
    Assert.That((int)DpxPacking.FilledA, Is.EqualTo(1));
    Assert.That((int)DpxPacking.FilledB, Is.EqualTo(2));

    var values = Enum.GetValues<DpxPacking>();
    Assert.That(values, Has.Length.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void DpxTransfer_HasExpectedValues() {
    Assert.That((int)DpxTransfer.UserDefined, Is.EqualTo(0));
    Assert.That((int)DpxTransfer.PrintingDensity, Is.EqualTo(1));
    Assert.That((int)DpxTransfer.Linear, Is.EqualTo(2));
    Assert.That((int)DpxTransfer.Logarithmic, Is.EqualTo(3));
    Assert.That((int)DpxTransfer.UnspecifiedVideo, Is.EqualTo(4));
    Assert.That((int)DpxTransfer.Smpte274M, Is.EqualTo(5));
    Assert.That((int)DpxTransfer.Itu709, Is.EqualTo(6));
    Assert.That((int)DpxTransfer.Itu601_625, Is.EqualTo(7));
    Assert.That((int)DpxTransfer.Itu601_525, Is.EqualTo(8));

    var values = Enum.GetValues<DpxTransfer>();
    Assert.That(values, Has.Length.EqualTo(9));
  }
}
