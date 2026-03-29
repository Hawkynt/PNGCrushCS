using System;
using System.Linq;
using FileFormat.ScitexCt;
using FileFormat.Core;

namespace FileFormat.ScitexCt.Tests;

[TestFixture]
public sealed class ScitexCtHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is80() {
    Assert.That(ScitexCtHeader.StructSize, Is.EqualTo(80));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_WriteTo_RoundTrip() {
    var original = new ScitexCtHeader(
      320,
      200,
      ScitexCtColorMode.Cmyk,
      8,
      0,
      300,
      300,
      "Test image"
    );

    var buffer = new byte[ScitexCtHeader.StructSize];
    original.WriteTo(buffer.AsSpan());
    var parsed = ScitexCtHeader.ReadFrom(buffer.AsSpan());

    Assert.That(parsed.Width, Is.EqualTo(original.Width));
    Assert.That(parsed.Height, Is.EqualTo(original.Height));
    Assert.That(parsed.ColorMode, Is.EqualTo(original.ColorMode));
    Assert.That(parsed.BitsPerComponent, Is.EqualTo(original.BitsPerComponent));
    Assert.That(parsed.HResolution, Is.EqualTo(original.HResolution));
    Assert.That(parsed.VResolution, Is.EqualTo(original.VResolution));
    Assert.That(parsed.Description, Is.EqualTo(original.Description));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversStructSize() {
    var map = ScitexCtHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(ScitexCtHeader.StructSize));
  }
}
