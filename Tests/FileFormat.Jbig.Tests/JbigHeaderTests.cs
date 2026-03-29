using System;
using System.Linq;
using FileFormat.Jbig;
using FileFormat.Core;

namespace FileFormat.Jbig.Tests;

[TestFixture]
public sealed class JbigHeaderTests {

  [Test]
  [Category("Unit")]
  public void StructSize_Is20() {
    Assert.That(JbigHeader.StructSize, Is.EqualTo(20));
  }

  [Test]
  [Category("Unit")]
  public void RoundTrip_PreservesAllFields() {
    var original = new JbigHeader(
      DL: 0,
      D: 0,
      P: 1,
      Reserved: 0,
      XD: 640,
      YD: 480,
      L0: 480,
      MX: 8,
      MY: 0,
      Options: JbigHeader.OptionTPBON,
      Order: 0
    );

    Span<byte> buffer = stackalloc byte[JbigHeader.StructSize];
    original.WriteTo(buffer);
    var parsed = JbigHeader.ReadFrom(buffer);

    Assert.That(parsed, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversStructSize() {
    var map = JbigHeader.GetFieldMap();
    var totalSize = map.Sum(f => f.Size);
    Assert.That(totalSize, Is.EqualTo(JbigHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_Has11Fields() {
    var map = JbigHeader.GetFieldMap();
    Assert.That(map, Has.Length.EqualTo(11));
  }
}
