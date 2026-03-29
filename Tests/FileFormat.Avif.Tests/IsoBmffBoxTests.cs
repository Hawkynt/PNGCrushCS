using System;
using System.Buffers.Binary;
using FileFormat.Avif;

namespace FileFormat.Avif.Tests;

[TestFixture]
public sealed class IsoBmffBoxTests {

  [Test]
  [Category("Unit")]
  public void FourCC_ValidString() {
    var value = IsoBmffBox.FourCC("ftyp");
    Assert.That(value, Is.EqualTo(0x66747970u));
  }

  [Test]
  [Category("Unit")]
  public void FourCC_WrongLength_ThrowsArgumentException() {
    Assert.Throws<ArgumentException>(() => IsoBmffBox.FourCC("ab"));
  }

  [Test]
  [Category("Unit")]
  public void FourCCToString_RoundTrip() {
    var original = "mdat";
    var value = IsoBmffBox.FourCC(original);
    var result = IsoBmffBox.FourCCToString(value);
    Assert.That(result, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void TypeConstants_AreCorrect() {
    Assert.That(IsoBmffBox.FourCCToString(IsoBmffBox.Ftyp), Is.EqualTo("ftyp"));
    Assert.That(IsoBmffBox.FourCCToString(IsoBmffBox.Meta), Is.EqualTo("meta"));
    Assert.That(IsoBmffBox.FourCCToString(IsoBmffBox.Mdat), Is.EqualTo("mdat"));
    Assert.That(IsoBmffBox.FourCCToString(IsoBmffBox.Ispe), Is.EqualTo("ispe"));
    Assert.That(IsoBmffBox.FourCCToString(IsoBmffBox.Hdlr), Is.EqualTo("hdlr"));
    Assert.That(IsoBmffBox.FourCCToString(IsoBmffBox.Pitm), Is.EqualTo("pitm"));
    Assert.That(IsoBmffBox.FourCCToString(IsoBmffBox.Iloc), Is.EqualTo("iloc"));
    Assert.That(IsoBmffBox.FourCCToString(IsoBmffBox.Iprp), Is.EqualTo("iprp"));
    Assert.That(IsoBmffBox.FourCCToString(IsoBmffBox.Ipco), Is.EqualTo("ipco"));
    Assert.That(IsoBmffBox.FourCCToString(IsoBmffBox.Ipma), Is.EqualTo("ipma"));
    Assert.That(IsoBmffBox.FourCCToString(IsoBmffBox.Av1C), Is.EqualTo("av1C"));
    Assert.That(IsoBmffBox.FourCCToString(IsoBmffBox.Pixi), Is.EqualTo("pixi"));
  }

  [Test]
  [Category("Unit")]
  public void ReadBoxes_SingleBox() {
    var payload = new byte[] { 0x01, 0x02, 0x03 };
    var box = IsoBmffBox.BuildBox(IsoBmffBox.Ftyp, payload);
    var boxes = IsoBmffBox.ReadBoxes(box, 0, box.Length);
    Assert.That(boxes.Count, Is.EqualTo(1));
    Assert.That(boxes[0].Type, Is.EqualTo(IsoBmffBox.Ftyp));
    Assert.That(boxes[0].Data, Is.EqualTo(payload));
  }

  [Test]
  [Category("Unit")]
  public void ReadBoxes_MultipleBoxes() {
    var box1 = IsoBmffBox.BuildBox(IsoBmffBox.Ftyp, [0x01]);
    var box2 = IsoBmffBox.BuildBox(IsoBmffBox.Mdat, [0x02, 0x03]);

    var combined = new byte[box1.Length + box2.Length];
    Array.Copy(box1, 0, combined, 0, box1.Length);
    Array.Copy(box2, 0, combined, box1.Length, box2.Length);

    var boxes = IsoBmffBox.ReadBoxes(combined, 0, combined.Length);
    Assert.That(boxes.Count, Is.EqualTo(2));
    Assert.That(boxes[0].Type, Is.EqualTo(IsoBmffBox.Ftyp));
    Assert.That(boxes[1].Type, Is.EqualTo(IsoBmffBox.Mdat));
  }

  [Test]
  [Category("Unit")]
  public void ReadBoxes_EmptyPayload() {
    var box = IsoBmffBox.BuildBox(IsoBmffBox.Meta, []);
    var boxes = IsoBmffBox.ReadBoxes(box, 0, box.Length);
    Assert.That(boxes.Count, Is.EqualTo(1));
    Assert.That(boxes[0].Data, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void WriteBox_CorrectSize() {
    var payload = new byte[] { 0xAA, 0xBB };
    var buffer = new byte[10];
    var written = IsoBmffBox.WriteBox(buffer, 0, IsoBmffBox.Mdat, payload);
    Assert.That(written, Is.EqualTo(10));
    var size = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(0));
    Assert.That(size, Is.EqualTo(10u));
  }

  [Test]
  [Category("Unit")]
  public void WriteBox_CorrectType() {
    var buffer = new byte[8];
    IsoBmffBox.WriteBox(buffer, 0, IsoBmffBox.Ftyp, []);
    var type = BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(4));
    Assert.That(type, Is.EqualTo(IsoBmffBox.Ftyp));
  }

  [Test]
  [Category("Unit")]
  public void BuildBox_RoundTrip() {
    var payload = new byte[] { 0x10, 0x20, 0x30, 0x40 };
    var box = IsoBmffBox.BuildBox(IsoBmffBox.Ispe, payload);
    var boxes = IsoBmffBox.ReadBoxes(box, 0, box.Length);
    Assert.That(boxes.Count, Is.EqualTo(1));
    Assert.That(boxes[0].Type, Is.EqualTo(IsoBmffBox.Ispe));
    Assert.That(boxes[0].Data, Is.EqualTo(payload));
  }

  [Test]
  [Category("Unit")]
  public void ReadBoxes_TruncatedData_StopsGracefully() {
    var box = IsoBmffBox.BuildBox(IsoBmffBox.Ftyp, [0x01, 0x02]);
    var truncated = new byte[box.Length - 2];
    Array.Copy(box, 0, truncated, 0, truncated.Length);
    var boxes = IsoBmffBox.ReadBoxes(truncated, 0, truncated.Length);
    Assert.That(boxes.Count, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ReadBoxes_PartialOffset() {
    var padding = new byte[4];
    var box = IsoBmffBox.BuildBox(IsoBmffBox.Mdat, [0x42]);
    var combined = new byte[padding.Length + box.Length];
    Array.Copy(padding, 0, combined, 0, padding.Length);
    Array.Copy(box, 0, combined, padding.Length, box.Length);
    var boxes = IsoBmffBox.ReadBoxes(combined, 4, box.Length);
    Assert.That(boxes.Count, Is.EqualTo(1));
    Assert.That(boxes[0].Type, Is.EqualTo(IsoBmffBox.Mdat));
  }
}
