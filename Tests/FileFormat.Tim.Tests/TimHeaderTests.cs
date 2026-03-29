using System;
using FileFormat.Tim;

namespace FileFormat.Tim.Tests;

[TestFixture]
public sealed class TimHeaderTests {

  [Test]
  [Category("Unit")]
  public void TimHeader_RoundTrip() {
    var original = new TimHeader(0x10, 0x0A);

    Span<byte> buf = stackalloc byte[TimHeader.StructSize];
    original.WriteTo(buf);
    var restored = TimHeader.ReadFrom(buf);

    Assert.That(restored.Magic, Is.EqualTo(original.Magic));
    Assert.That(restored.Flags, Is.EqualTo(original.Flags));
  }

  [Test]
  [Category("Unit")]
  public void TimHeader_GetFieldMap_ReturnsExpectedFields() {
    var fields = TimHeader.GetFieldMap();

    Assert.That(fields, Has.Length.EqualTo(2));
    Assert.That(fields[0].Name, Is.EqualTo("Magic"));
    Assert.That(fields[0].Offset, Is.EqualTo(0));
    Assert.That(fields[0].Size, Is.EqualTo(4));
    Assert.That(fields[1].Name, Is.EqualTo("Flags"));
    Assert.That(fields[1].Offset, Is.EqualTo(4));
    Assert.That(fields[1].Size, Is.EqualTo(4));
  }

  [Test]
  [Category("Unit")]
  public void TimHeader_StructSize_Is8() {
    Assert.That(TimHeader.StructSize, Is.EqualTo(8));
  }

  [Test]
  [Category("Unit")]
  public void TimBlockHeader_RoundTrip() {
    var original = new TimBlockHeader(1024, 10, 20, 128, 64);

    Span<byte> buf = stackalloc byte[TimBlockHeader.StructSize];
    original.WriteTo(buf);
    var restored = TimBlockHeader.ReadFrom(buf);

    Assert.That(restored.BlockSize, Is.EqualTo(original.BlockSize));
    Assert.That(restored.X, Is.EqualTo(original.X));
    Assert.That(restored.Y, Is.EqualTo(original.Y));
    Assert.That(restored.Width, Is.EqualTo(original.Width));
    Assert.That(restored.Height, Is.EqualTo(original.Height));
  }

  [Test]
  [Category("Unit")]
  public void TimBlockHeader_StructSize_Is12() {
    Assert.That(TimBlockHeader.StructSize, Is.EqualTo(12));
  }
}
