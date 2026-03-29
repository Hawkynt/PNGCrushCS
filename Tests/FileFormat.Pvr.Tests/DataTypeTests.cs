using System;
using FileFormat.Pvr;

namespace FileFormat.Pvr.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PvrPixelFormat_HasExpectedValues() {
    Assert.That((ulong)PvrPixelFormat.PVRTC_2BPP_RGB, Is.EqualTo(0UL));
    Assert.That((ulong)PvrPixelFormat.PVRTC_2BPP_RGBA, Is.EqualTo(1UL));
    Assert.That((ulong)PvrPixelFormat.PVRTC_4BPP_RGB, Is.EqualTo(2UL));
    Assert.That((ulong)PvrPixelFormat.PVRTC_4BPP_RGBA, Is.EqualTo(3UL));
    Assert.That((ulong)PvrPixelFormat.ETC1, Is.EqualTo(6UL));
    Assert.That((ulong)PvrPixelFormat.ETC2_RGB, Is.EqualTo(22UL));
    Assert.That((ulong)PvrPixelFormat.ETC2_RGBA, Is.EqualTo(23UL));
    Assert.That((ulong)PvrPixelFormat.ASTC_4x4, Is.EqualTo(27UL));

    var values = Enum.GetValues<PvrPixelFormat>();
    Assert.That(values, Has.Length.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void PvrColorSpace_HasExpectedValues() {
    Assert.That((int)PvrColorSpace.Linear, Is.EqualTo(0));
    Assert.That((int)PvrColorSpace.Srgb, Is.EqualTo(1));

    var values = Enum.GetValues<PvrColorSpace>();
    Assert.That(values, Has.Length.EqualTo(2));
  }
}
