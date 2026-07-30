using System;
using System.Buffers.Binary;
using FileFormat.Jpeg2000;

namespace FileFormat.Jpeg2000.Tests;

[TestFixture]
public sealed class Jp2BoxTests {

  [Test]
  [Category("Unit")]
  public void ReadBoxes_SingleBox_ParsesTypeAndData() {
    var data = new byte[16];
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0), 16); // length
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), 0x74657374); // "test"
    data[8] = 0xAA;
    data[9] = 0xBB;
    data[10] = 0xCC;
    data[11] = 0xDD;

    var boxes = Jp2Box.ReadBoxes(data, 0, data.Length);

    Assert.That(boxes, Has.Count.EqualTo(1));
    Assert.That(boxes[0].Type, Is.EqualTo(0x74657374u));
    Assert.That(boxes[0].Data, Has.Length.EqualTo(8));
    Assert.That(boxes[0].Data[0], Is.EqualTo(0xAA));
  }

  [Test]
  [Category("Unit")]
  public void ReadBoxes_MultipleBoxes_ParsesAll() {
    var data = new byte[24];
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0), 12);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), 0x41414141); // "AAAA"
    data[8] = 1; data[9] = 2; data[10] = 3; data[11] = 4;

    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(12), 12);
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(16), 0x42424242); // "BBBB"
    data[20] = 5; data[21] = 6; data[22] = 7; data[23] = 8;

    var boxes = Jp2Box.ReadBoxes(data, 0, data.Length);

    Assert.That(boxes, Has.Count.EqualTo(2));
    Assert.That(boxes[0].Type, Is.EqualTo(0x41414141u));
    Assert.That(boxes[1].Type, Is.EqualTo(0x42424242u));
  }

  [Test]
  [Category("Unit")]
  public void ReadBoxes_BoxLength0_ExtendsToEnd() {
    var data = new byte[20];
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0), 0); // extends to end
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), 0x74657374);
    for (var i = 8; i < 20; ++i)
      data[i] = (byte)i;

    var boxes = Jp2Box.ReadBoxes(data, 0, data.Length);

    Assert.That(boxes, Has.Count.EqualTo(1));
    Assert.That(boxes[0].Data, Has.Length.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void TypeFromString_ValidFourCC_ReturnsCorrectValue() {
    var result = Jp2Box.TypeFromString("jp2c");
    Assert.That(result, Is.EqualTo(Jp2Box.TYPE_CODESTREAM));
  }

  [Test]
  [Category("Unit")]
  public void TypeFromString_WrongLength_ThrowsArgumentException() {
    Assert.Throws<ArgumentException>(() => Jp2Box.TypeFromString("abc"));
  }

  [Test]
  [Category("Unit")]
  public void TypeToString_RoundTrips() {
    var original = "jp2h";
    var type = Jp2Box.TypeFromString(original);
    var result = Jp2Box.TypeToString(type);

    Assert.That(result, Is.EqualTo(original));
  }

  [Test]
  [Category("Unit")]
  public void JP2_SIGNATURE_BYTES_HasCorrectLength() {
    Assert.That(Jp2Box.JP2_SIGNATURE_BYTES, Has.Length.EqualTo(12));
  }

  [Test]
  [Category("Unit")]
  public void JP2_SIGNATURE_BYTES_HasCorrectContent() {
    Assert.That(Jp2Box.JP2_SIGNATURE_BYTES[4], Is.EqualTo(0x6A)); // 'j'
    Assert.That(Jp2Box.JP2_SIGNATURE_BYTES[5], Is.EqualTo(0x50)); // 'P'
    Assert.That(Jp2Box.JP2_SIGNATURE_BYTES[6], Is.EqualTo(0x20)); // ' '
    Assert.That(Jp2Box.JP2_SIGNATURE_BYTES[7], Is.EqualTo(0x20)); // ' '
  }
}
