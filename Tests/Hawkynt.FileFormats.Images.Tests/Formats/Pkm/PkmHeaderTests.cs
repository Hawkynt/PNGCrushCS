using System;
using FileFormat.Pkm;

namespace FileFormat.Pkm.Tests;

[TestFixture]
public sealed class PkmHeaderTests {

  [Test]
  [Category("Unit")]
  public void ReadFromWriteTo_RoundTrip() {
    var original = new PkmHeader(
      Magic1: (byte)'P',
      Magic2: (byte)'K',
      Magic3: (byte)'M',
      Magic4: (byte)' ',
      Version1: (byte)'2',
      Version2: (byte)'0',
      Format: 3,
      PaddedWidth: 128,
      PaddedHeight: 64,
      Width: 100,
      Height: 50
    );

    var buffer = new byte[PkmHeader.StructSize];
    original.WriteTo(buffer);
    var restored = PkmHeader.ReadFrom(buffer);

    Assert.That(restored.Magic1, Is.EqualTo(original.Magic1));
    Assert.That(restored.Magic2, Is.EqualTo(original.Magic2));
    Assert.That(restored.Magic3, Is.EqualTo(original.Magic3));
    Assert.That(restored.Magic4, Is.EqualTo(original.Magic4));
    Assert.That(restored.Version1, Is.EqualTo(original.Version1));
    Assert.That(restored.Version2, Is.EqualTo(original.Version2));
    Assert.That(restored.Format, Is.EqualTo(original.Format));
    Assert.That(restored.PaddedWidth, Is.EqualTo(original.PaddedWidth));
    Assert.That(restored.PaddedHeight, Is.EqualTo(original.PaddedHeight));
    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
  }

  [Test]
  [Category("Unit")]
  public void GetFieldMap_CoversFull16Bytes() {
    var fields = PkmHeader.GetFieldMap();
    var totalCoverage = 0;
    foreach (var field in fields)
      totalCoverage += field.Size;

    Assert.That(totalCoverage, Is.EqualTo(PkmHeader.StructSize));
  }

  [Test]
  [Category("Unit")]
  public void StructSize_Is16() {
    Assert.That(PkmHeader.StructSize, Is.EqualTo(16));
  }
}
