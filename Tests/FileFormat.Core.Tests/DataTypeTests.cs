using FileFormat.Core;

namespace FileFormat.Core.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  public void PixelFormat_Has14Values() {
    Assert.That(System.Enum.GetValues<PixelFormat>(), Has.Length.EqualTo(14));
  }

  [Test]
  public void Endianness_HasLittleAndBig() {
    Assert.That(System.Enum.GetValues<Endianness>(), Has.Length.EqualTo(2));
    Assert.That((int)Endianness.Little, Is.EqualTo(0));
    Assert.That((int)Endianness.Big, Is.EqualTo(1));
  }

  [Test]
  public void GenerateSerializerAttribute_IsAttributeTargetStruct() {
    var attr = new GenerateSerializerAttribute();
    var usage = (System.AttributeUsageAttribute)System.Attribute.GetCustomAttribute(typeof(GenerateSerializerAttribute), typeof(System.AttributeUsageAttribute))!;
    Assert.That(usage.ValidOn, Is.EqualTo(System.AttributeTargets.Struct));
  }

  [Test]
  public void HeaderFieldAttribute_NewProperties_HaveDefaults() {
    var attr = new HeaderFieldAttribute(0, 4);

    Assert.That(attr.Endianness, Is.EqualTo(Endianness.Little));
    Assert.That(attr.EndianFieldName, Is.Null);
    Assert.That(attr.ArrayLength, Is.EqualTo(0));
    Assert.That(attr.BitOffset, Is.EqualTo(-1));
    Assert.That(attr.BitCount, Is.EqualTo(0));
  }

  [Test]
  public void HeaderFieldAttribute_SetEndianness_BigEndian() {
    var attr = new HeaderFieldAttribute(0, 4) { Endianness = Endianness.Big };

    Assert.That(attr.Endianness, Is.EqualTo(Endianness.Big));
  }

  [Test]
  public void HeaderFieldAttribute_SetArrayLength() {
    var attr = new HeaderFieldAttribute(0, 48) { ArrayLength = 16 };

    Assert.That(attr.ArrayLength, Is.EqualTo(16));
  }

  [Test]
  public void HeaderFieldAttribute_SetBitFields() {
    var attr = new HeaderFieldAttribute(0, 1) { BitOffset = 3, BitCount = 4 };

    Assert.That(attr.BitOffset, Is.EqualTo(3));
    Assert.That(attr.BitCount, Is.EqualTo(4));
  }

  [Test]
  public void HeaderFieldAttribute_SetEndianFieldName() {
    var attr = new HeaderFieldAttribute(0, 4) { EndianFieldName = "isBigEndian" };

    Assert.That(attr.EndianFieldName, Is.EqualTo("isBigEndian"));
  }

  [Test]
  public void HeaderSerializer_SizeOf_ReturnsSerializedSize() {
    var size = HeaderSerializer.SizeOf<FileFormat.Msp.MspHeader>();

    Assert.That(size, Is.EqualTo(32));
  }

  [Test]
  public void HeaderSerializer_ReadWrite_RoundTrip() {
    var original = new FileFormat.Msp.MspHeader(0x6144, 0x4D6E, 320, 200, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    var buffer = new byte[32];
    HeaderSerializer.Write(original, buffer);

    var read = HeaderSerializer.Read<FileFormat.Msp.MspHeader>(buffer);

    Assert.That(read.Key1, Is.EqualTo(original.Key1));
    Assert.That(read.Width, Is.EqualTo(original.Width));
    Assert.That(read.Height, Is.EqualTo(original.Height));
  }

  [Test]
  public void IBinarySerializable_ImplementedByGeneratedHeaders() {
    Assert.That(typeof(FileFormat.Core.IBinarySerializable<FileFormat.Msp.MspHeader>).IsAssignableFrom(typeof(FileFormat.Msp.MspHeader)));
  }

  [Test]
  public void RawImage_Defaults_NullOptionalFields() {
    var img = new RawImage {
      Width = 1, Height = 1,
      Format = PixelFormat.Gray8,
      PixelData = [0],
    };

    Assert.That(img.Palette, Is.Null);
    Assert.That(img.AlphaTable, Is.Null);
    Assert.That(img.PaletteCount, Is.EqualTo(0));
  }
}
