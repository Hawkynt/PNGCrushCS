using System;
using System.Buffers.Binary;
using System.Text;
using FileFormat.IffAnim;

namespace FileFormat.IffAnim.Tests;

[TestFixture]
public sealed class IffAnimWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => IffAnimWriter.ToBytes(null!));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FormTag() {
    var file = new IffAnimFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = IffAnimWriter.ToBytes(file);

    var formTag = Encoding.ASCII.GetString(bytes, 0, 4);
    Assert.That(formTag, Is.EqualTo("FORM"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AnimFormType() {
    var file = new IffAnimFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = IffAnimWriter.ToBytes(file);

    var formType = Encoding.ASCII.GetString(bytes, 8, 4);
    Assert.That(formType, Is.EqualTo("ANIM"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsIlbmSubForm() {
    var file = new IffAnimFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = IffAnimWriter.ToBytes(file);

    // The embedded ILBM starts at offset 12
    Assert.That(bytes.Length, Is.GreaterThan(24));
    var ilbmFormTag = Encoding.ASCII.GetString(bytes, 12, 4);
    var ilbmFormType = Encoding.ASCII.GetString(bytes, 20, 4);
    Assert.That(ilbmFormTag, Is.EqualTo("FORM"));
    Assert.That(ilbmFormType, Is.EqualTo("ILBM"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_FormSizeFieldIsConsistent() {
    var file = new IffAnimFile {
      Width = 2,
      Height = 2,
      PixelData = new byte[2 * 2 * 3]
    };

    var bytes = IffAnimWriter.ToBytes(file);

    var formSize = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(4));
    Assert.That(formSize, Is.EqualTo(bytes.Length - 8));
  }
}
