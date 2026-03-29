using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.Heif;

namespace FileFormat.Heif.Tests;

[TestFixture]
public sealed class IsoBmffBoxTests {

  [Test]
  [Category("Unit")]
  public void ReadBoxes_SingleBox() {
    var payload = new byte[] { 0xAA, 0xBB, 0xCC };
    var box = _MakeBox("test", payload);

    var boxes = IsoBmffBox.ReadBoxes(box, 0, box.Length);

    Assert.That(boxes, Has.Count.EqualTo(1));
    Assert.That(boxes[0].Type, Is.EqualTo("test"));
    Assert.That(boxes[0].Data, Is.EqualTo(payload));
  }

  [Test]
  [Category("Unit")]
  public void ReadBoxes_MultipleBoxes() {
    var box1 = _MakeBox("aaaa", [0x01, 0x02]);
    var box2 = _MakeBox("bbbb", [0x03, 0x04, 0x05]);
    var data = new byte[box1.Length + box2.Length];
    Array.Copy(box1, 0, data, 0, box1.Length);
    Array.Copy(box2, 0, data, box1.Length, box2.Length);

    var boxes = IsoBmffBox.ReadBoxes(data, 0, data.Length);

    Assert.That(boxes, Has.Count.EqualTo(2));
    Assert.That(boxes[0].Type, Is.EqualTo("aaaa"));
    Assert.That(boxes[1].Type, Is.EqualTo("bbbb"));
  }

  [Test]
  [Category("Unit")]
  public void ReadBoxes_SizeZeroExtendsToEnd() {
    // size=0 means box extends to end of data
    var data = new byte[16];
    BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0), 0);
    Encoding.ASCII.GetBytes("endx", 0, 4, data, 4);
    data[8] = 0xDE;
    data[9] = 0xAD;

    var boxes = IsoBmffBox.ReadBoxes(data, 0, data.Length);

    Assert.That(boxes, Has.Count.EqualTo(1));
    Assert.That(boxes[0].Type, Is.EqualTo("endx"));
    Assert.That(boxes[0].Data.Length, Is.EqualTo(8)); // 16 - 8 header
  }

  [Test]
  [Category("Unit")]
  public void ReadBoxes_EmptyData_ReturnsEmpty() {
    var boxes = IsoBmffBox.ReadBoxes([], 0, 0);
    Assert.That(boxes, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void ReadBoxes_InsufficientHeaderData_ReturnsEmpty() {
    var data = new byte[4]; // less than 8-byte header
    var boxes = IsoBmffBox.ReadBoxes(data, 0, data.Length);
    Assert.That(boxes, Is.Empty);
  }

  [Test]
  [Category("Unit")]
  public void WriteBox_ProducesCorrectStructure() {
    var payload = new byte[] { 0x11, 0x22, 0x33 };
    var box = IsoBmffBox.WriteBox("tbox", payload);

    var size = BinaryPrimitives.ReadUInt32BigEndian(box.AsSpan(0));
    var type = Encoding.ASCII.GetString(box, 4, 4);

    Assert.That(size, Is.EqualTo(11u)); // 8 header + 3 data
    Assert.That(type, Is.EqualTo("tbox"));
    Assert.That(box[8], Is.EqualTo(0x11));
    Assert.That(box[9], Is.EqualTo(0x22));
    Assert.That(box[10], Is.EqualTo(0x33));
  }

  [Test]
  [Category("Unit")]
  public void WriteBox_EmptyPayload() {
    var box = IsoBmffBox.WriteBox("none", []);

    Assert.That(box.Length, Is.EqualTo(8));
    var size = BinaryPrimitives.ReadUInt32BigEndian(box.AsSpan(0));
    Assert.That(size, Is.EqualTo(8u));
  }

  [Test]
  [Category("Unit")]
  public void WriteFullBox_IncludesVersionAndFlags() {
    var payload = new byte[] { 0xAA };
    var box = IsoBmffBox.WriteFullBox("full", 1, 0x020304, payload);

    Assert.That(box[8], Is.EqualTo(1)); // version
    Assert.That(box[9], Is.EqualTo(0x02)); // flags[0]
    Assert.That(box[10], Is.EqualTo(0x03)); // flags[1]
    Assert.That(box[11], Is.EqualTo(0x04)); // flags[2]
    Assert.That(box[12], Is.EqualTo(0xAA)); // payload
  }

  [Test]
  [Category("Unit")]
  public void WriteBox_ThenReadBoxes_RoundTrip() {
    var payload = new byte[] { 0x10, 0x20, 0x30, 0x40 };
    var box = IsoBmffBox.WriteBox("trip", payload);
    var boxes = IsoBmffBox.ReadBoxes(box, 0, box.Length);

    Assert.That(boxes, Has.Count.EqualTo(1));
    Assert.That(boxes[0].Type, Is.EqualTo("trip"));
    Assert.That(boxes[0].Data, Is.EqualTo(payload));
  }

  [Test]
  [Category("Unit")]
  public void BoxTypeConstants_AreExpectedValues() {
    Assert.That(IsoBmffBox.Ftyp, Is.EqualTo("ftyp"));
    Assert.That(IsoBmffBox.Meta, Is.EqualTo("meta"));
    Assert.That(IsoBmffBox.Hdlr, Is.EqualTo("hdlr"));
    Assert.That(IsoBmffBox.Pitm, Is.EqualTo("pitm"));
    Assert.That(IsoBmffBox.Iloc, Is.EqualTo("iloc"));
    Assert.That(IsoBmffBox.Iprp, Is.EqualTo("iprp"));
    Assert.That(IsoBmffBox.Ipco, Is.EqualTo("ipco"));
    Assert.That(IsoBmffBox.Ipma, Is.EqualTo("ipma"));
    Assert.That(IsoBmffBox.Ispe, Is.EqualTo("ispe"));
    Assert.That(IsoBmffBox.Mdat, Is.EqualTo("mdat"));
  }

  // --- Helpers ---

  private static byte[] _MakeBox(string type, byte[] payload) {
    var totalSize = 8 + payload.Length;
    var result = new byte[totalSize];
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(0), (uint)totalSize);
    Encoding.ASCII.GetBytes(type, 0, 4, result, 4);
    Array.Copy(payload, 0, result, 8, payload.Length);
    return result;
  }
}
