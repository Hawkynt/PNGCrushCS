using System;
using FileFormat.Vtf;

namespace FileFormat.Vtf.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void VtfFormat_HasExpectedValues() {
    Assert.That((int)VtfFormat.Rgba8888, Is.EqualTo(0));
    Assert.That((int)VtfFormat.Abgr8888, Is.EqualTo(1));
    Assert.That((int)VtfFormat.Rgb888, Is.EqualTo(2));
    Assert.That((int)VtfFormat.Bgr888, Is.EqualTo(3));
    Assert.That((int)VtfFormat.Rgb565, Is.EqualTo(4));
    Assert.That((int)VtfFormat.I8, Is.EqualTo(5));
    Assert.That((int)VtfFormat.Ia88, Is.EqualTo(6));
    Assert.That((int)VtfFormat.A8, Is.EqualTo(7));
    Assert.That((int)VtfFormat.Rgb888Bluescreen, Is.EqualTo(9));
    Assert.That((int)VtfFormat.Bgr888Bluescreen, Is.EqualTo(10));
    Assert.That((int)VtfFormat.Argb8888, Is.EqualTo(11));
    Assert.That((int)VtfFormat.Bgra8888, Is.EqualTo(12));
    Assert.That((int)VtfFormat.Dxt1, Is.EqualTo(13));
    Assert.That((int)VtfFormat.Dxt3, Is.EqualTo(14));
    Assert.That((int)VtfFormat.Dxt5, Is.EqualTo(15));
    Assert.That((int)VtfFormat.Uv88, Is.EqualTo(16));
    Assert.That((int)VtfFormat.Rgba16161616F, Is.EqualTo(24));
    Assert.That((int)VtfFormat.Rgba16161616, Is.EqualTo(25));
    Assert.That((int)VtfFormat.None, Is.EqualTo(-1));

    var values = Enum.GetValues<VtfFormat>();
    Assert.That(values, Has.Length.EqualTo(19));
  }

  [Test]
  [Category("Unit")]
  public void VtfFlags_HasExpectedValues() {
    Assert.That((int)VtfFlags.None, Is.EqualTo(0));
    Assert.That((int)VtfFlags.PointSampling, Is.EqualTo(0x1));
    Assert.That((int)VtfFlags.Trilinear, Is.EqualTo(0x2));
    Assert.That((int)VtfFlags.ClampS, Is.EqualTo(0x4));
    Assert.That((int)VtfFlags.ClampT, Is.EqualTo(0x8));
    Assert.That((int)VtfFlags.Anisotropic, Is.EqualTo(0x10));
    Assert.That((int)VtfFlags.NoMipmap, Is.EqualTo(0x100));
    Assert.That((int)VtfFlags.NoLod, Is.EqualTo(0x200));
    Assert.That((int)VtfFlags.Srgb, Is.EqualTo(0x40000000));

    var values = Enum.GetValues<VtfFlags>();
    Assert.That(values, Has.Length.EqualTo(9));
  }

  [Test]
  [Category("Unit")]
  public void VtfFlags_IsFlagsEnum() {
    var combined = VtfFlags.Trilinear | VtfFlags.Anisotropic;
    Assert.That(combined.HasFlag(VtfFlags.Trilinear), Is.True);
    Assert.That(combined.HasFlag(VtfFlags.Anisotropic), Is.True);
    Assert.That(combined.HasFlag(VtfFlags.PointSampling), Is.False);
  }
}
