using System;
using System.Buffers.Binary;
using FileFormat.Avif;

namespace FileFormat.Avif.Tests;

[TestFixture]
public sealed class AvifWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => AvifWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsFtypBox() {
    var file = new AvifFile { Width = 1, Height = 1, PixelData = new byte[3] };
    var bytes = AvifWriter.ToBytes(file);
    var type = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4));
    Assert.That(type, Is.EqualTo(IsoBmffBox.Ftyp));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FtypBrandIsAvif() {
    var file = new AvifFile { Width = 1, Height = 1, PixelData = new byte[3] };
    var bytes = AvifWriter.ToBytes(file);
    var brand = System.Text.Encoding.ASCII.GetString(bytes, 8, 4);
    Assert.That(brand, Is.EqualTo("avif"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsMetaBox() {
    var file = new AvifFile { Width = 1, Height = 1, PixelData = new byte[3] };
    var bytes = AvifWriter.ToBytes(file);
    var boxes = IsoBmffBox.ReadBoxes(bytes, 0, bytes.Length);
    Assert.That(boxes.Exists(b => b.Type == IsoBmffBox.Meta), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsMdatBox() {
    var file = new AvifFile { Width = 1, Height = 1, PixelData = new byte[3] };
    var bytes = AvifWriter.ToBytes(file);
    var boxes = IsoBmffBox.ReadBoxes(bytes, 0, bytes.Length);
    Assert.That(boxes.Exists(b => b.Type == IsoBmffBox.Mdat), Is.True);
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_IspeContainsDimensions() {
    var file = new AvifFile { Width = 640, Height = 480, PixelData = new byte[640 * 480 * 3] };
    var bytes = AvifWriter.ToBytes(file);
    var result = AvifReader.FromBytes(bytes);
    Assert.That(result.Width, Is.EqualTo(640));
    Assert.That(result.Height, Is.EqualTo(480));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MdatContainsPixelData() {
    var pixels = new byte[] { 0xAA, 0xBB, 0xCC };
    var file = new AvifFile { Width = 1, Height = 1, PixelData = pixels, RawImageData = pixels };
    var bytes = AvifWriter.ToBytes(file);
    var boxes = IsoBmffBox.ReadBoxes(bytes, 0, bytes.Length);
    var mdat = boxes.Find(b => b.Type == IsoBmffBox.Mdat);
    Assert.That(mdat, Is.Not.Null);
    Assert.That(mdat!.Data, Is.EqualTo(pixels));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FileSize_GreaterThanHeader() {
    var file = new AvifFile { Width = 2, Height = 2, PixelData = new byte[12], RawImageData = new byte[12] };
    var bytes = AvifWriter.ToBytes(file);
    Assert.That(bytes.Length, Is.GreaterThan(12));
  }
}
