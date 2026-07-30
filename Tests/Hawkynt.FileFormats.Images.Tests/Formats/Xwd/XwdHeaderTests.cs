using System;
using System.Linq;
using FileFormat.Xwd;
using FileFormat.Core;

namespace FileFormat.Xwd.Tests;

[TestFixture]
public sealed class XwdHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is100() {
    Assert.That(XwdHeader.StructSize, Is.EqualTo(100));
  }

  [Test]
  [Category("Unit")]
  public void ReadFrom_WriteTo_RoundTrip() {
    var original = new XwdHeader(
      200, 7, 2, 24, 640, 480, 0, 1, 32, 1, 32, 24, 1920,
      4, 0x00FF0000, 0x0000FF00, 0x000000FF, 8, 256, 256,
      640, 480, -15, 30, 5
    );
    var buffer = new byte[XwdHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = XwdHeader.ReadFrom(buffer);
    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversStructSize() {
    var map = XwdHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(XwdHeader.StructSize));
    Assert.That(map, Has.Length.EqualTo(25));
  }
}
