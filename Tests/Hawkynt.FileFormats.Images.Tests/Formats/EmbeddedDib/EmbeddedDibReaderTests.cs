using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace FileFormat.EmbeddedDib.Tests;

[TestFixture]
public sealed class EmbeddedDibReaderTests {

  [Test]
  [Category("Unit")]
  public void FromSpan_PackedDib_StartsAtByteZero() {
    var source = _Image();
    var bytes = EmbeddedDibWriter.ToBytes(source);

    var file = EmbeddedDibReader.FromSpan(bytes);

    Assert.Multiple(() => {
      Assert.That(file.Offset, Is.Zero);
      Assert.That(file.Preview.Width, Is.EqualTo(source.Width));
      Assert.That(file.Preview.Height, Is.EqualTo(source.Height));
      Assert.That(file.Preview.Format, Is.EqualTo(source.Format));
      Assert.That(file.Preview.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_DoesNotSearchThroughAnUnrelatedContainer() {
    var dib = EmbeddedDibWriter.ToBytes(_Image());
    var container = new byte[11 + dib.Length];
    dib.CopyTo(container.AsSpan(11));

    Assert.That(() => EmbeddedDibReader.FromSpan(container), Throws.TypeOf<InvalidDataException>());

    var found = EmbeddedDibReader.FindInSpan(container);
    Assert.That(found.Offset, Is.EqualTo(11));
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_Bitfields40Header_AccountsForThreeMasks() {
    var bytes = new byte[40 + 12 + 4];
    BinaryPrimitives.WriteInt32LittleEndian(bytes, 40);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 2);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), 16);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 3); // BI_BITFIELDS
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), 4);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 0xF800);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(44), 0x07E0);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), 0x001F);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(52), 0xF800);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(54), 0x07E0);

    var decoded = EmbeddedDibReader.FromSpan(bytes).Preview;

    Assert.Multiple(() => {
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Bgr24));
      Assert.That(decoded.PixelData, Is.EqualTo(new byte[] { 0, 0, 255, 0, 255, 0 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromSpan_AlphaBitfields40Header_AccountsForFourthMask() {
    var bytes = new byte[40 + 16 + 4];
    BinaryPrimitives.WriteInt32LittleEndian(bytes, 40);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), 1);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12), 1);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), 32);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 6); // BI_ALPHABITFIELDS
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), 4);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(40), 0x00FF0000);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(44), 0x0000FF00);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), 0x000000FF);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52), 0xFF000000);
    bytes[56] = 0x10;
    bytes[57] = 0x20;
    bytes[58] = 0x40;
    bytes[59] = 0x80;

    var decoded = EmbeddedDibReader.FromSpan(bytes).Preview;

    Assert.Multiple(() => {
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Bgra32));
      Assert.That(decoded.PixelData, Is.EqualTo(new byte[] { 0x10, 0x20, 0x40, 0x80 }));
    });
  }

  [Test]
  [Category("Unit")]
  public void Registry_EmbeddedDib_IsTheWritableDotDibFormat() {
    var entry = FormatRegistry.GetEntry(ImageFormat.EmbeddedDib);
    Assert.That(entry, Is.Not.Null);

    var bytes = FormatRegistry.Write(_Image(), ImageFormat.EmbeddedDib);
    Assert.That(bytes, Is.Not.Null.And.Not.Empty);
    var decoded = entry!.LoadRawImageFromBytes(bytes!);

    Assert.Multiple(() => {
      Assert.That(entry.PrimaryExtension, Is.EqualTo(".dib"));
      Assert.That(entry.AllExtensions, Is.EqualTo(new[] { ".dib" }));
      Assert.That(entry.SupportsWrite, Is.True);
      Assert.That(bytes![..2], Is.Not.EqualTo(new byte[] { (byte)'B', (byte)'M' }));
      Assert.That(decoded, Is.Not.Null);
      Assert.That((decoded!.Width, decoded.Height), Is.EqualTo((_Image().Width, _Image().Height)));
    });
  }

  private static RawImage _Image() => new() {
    Width = 2,
    Height = 2,
    Format = PixelFormat.Bgr24,
    PixelData = [
      0x03, 0x02, 0x01, 0x06, 0x05, 0x04,
      0x09, 0x08, 0x07, 0x0C, 0x0B, 0x0A,
    ]
  };
}
