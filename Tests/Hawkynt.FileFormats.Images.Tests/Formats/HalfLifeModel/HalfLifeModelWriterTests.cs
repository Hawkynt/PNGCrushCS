using System;
using System.Buffers.Binary;
using FileFormat.Core;
using FileFormat.HalfLifeModel;

namespace FileFormat.HalfLifeModel.Tests;

[TestFixture]
public sealed class HalfLifeModelWriterTests {

  [Test]
  [Category("Unit")]
  public void Writer_EmitsTextureOnlyStudioModelLayout() {
    var file = _File(16, 12, "skin");
    var bytes = HalfLifeModelWriter.ToBytes(file);

    // Literal offsets pin the published v10 studiohdr_t layout independently of our constants.
    Assert.Multiple(() => {
      Assert.That(bytes.AsSpan(0, 4).SequenceEqual("IDST"u8), Is.True);
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)), Is.EqualTo(10));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x48)), Is.EqualTo(bytes.Length));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0xB4)), Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0xB8)), Is.EqualTo(0xF4));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0xBC)), Is.EqualTo(0x148));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0xC0)), Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0xC4)), Is.EqualTo(1));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0xC8)), Is.EqualTo(0x144));
    });

    // Copied out of the span rather than read through it: a ref struct local cannot be captured by
    // the lambda Assert.Multiple takes.
    var entry = bytes.AsSpan(0xF4, 80).ToArray();
    Assert.Multiple(() => {
      Assert.That(entry[..5], Is.EqualTo(new byte[] { (byte)'s', (byte)'k', (byte)'i', (byte)'n', 0 }));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(entry.AsSpan(64)), Is.Zero);
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(entry.AsSpan(68)), Is.EqualTo(16));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(entry.AsSpan(72)), Is.EqualTo(12));
      Assert.That(BinaryPrimitives.ReadInt32LittleEndian(entry.AsSpan(76)), Is.EqualTo(0x148));
      Assert.That(BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(0x144)), Is.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_RoundTripsPixelsPaletteAndName() {
    var source = _File(31, 17, "roundtrip");
    var actual = HalfLifeModelReader.FromBytes(HalfLifeModelWriter.ToBytes(source));

    Assert.Multiple(() => {
      Assert.That(actual.Width, Is.EqualTo(source.Width));
      Assert.That(actual.Height, Is.EqualTo(source.Height));
      Assert.That(actual.SkinCount, Is.EqualTo(1));
      Assert.That(actual.Name, Is.EqualTo(source.Name));
      Assert.That(actual.PixelData, Is.EqualTo(source.PixelData));
      Assert.That(actual.Palette, Is.EqualTo(source.Palette));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_WritesAnArbitraryTrueColourImage() {
    const int width = 16, height = 12;
    var pixels = new byte[width * height * 4];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 4] = (byte)(i * 3);
      pixels[i * 4 + 1] = (byte)(255 - i * 5);
      pixels[i * 4 + 2] = (byte)(i * 7);
      pixels[i * 4 + 3] = 255;
    }

    var source = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgba32,
      PixelData = pixels,
    };

    var file = HalfLifeModelFile.FromRawImage(source);
    var decoded = HalfLifeModelFile.ToRawImage(HalfLifeModelReader.FromBytes(HalfLifeModelWriter.ToBytes(file)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(width));
      Assert.That(decoded.Height, Is.EqualTo(height));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Indexed8));
      Assert.That(decoded.PaletteCount, Is.EqualTo(HalfLifeModelFile.PaletteEntries));
      Assert.That(decoded.PixelData, Has.Length.EqualTo(width * height));
      Assert.That(decoded.Palette, Has.Length.EqualTo(HalfLifeModelFile.PaletteEntries * 3));
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_RefusesMalformedBuffersNamesAndVanillaOversize() {
    var file = _File(16, 12, "skin");

    Assert.Multiple(() => {
      Assert.Throws<ArgumentException>(() => HalfLifeModelWriter.ToBytes(file with { PixelData = new byte[191] }));
      Assert.Throws<ArgumentException>(() => HalfLifeModelWriter.ToBytes(file with { Palette = new byte[767] }));
      Assert.Throws<ArgumentException>(() => HalfLifeModelWriter.ToBytes(file with { Name = new string('x', 64) }));
      Assert.Throws<ArgumentException>(() => HalfLifeModelWriter.ToBytes(file with { Name = "skín" }));
      Assert.Throws<ArgumentOutOfRangeException>(() => HalfLifeModelWriter.ToBytes(_File(7, 16, "too-small")));
      Assert.Throws<ArgumentOutOfRangeException>(() => HalfLifeModelWriter.ToBytes(_File(513, 16, "too-large")));
    });
  }

  private static HalfLifeModelFile _File(int width, int height, string name) {
    var pixels = new byte[width * height];
    for (var i = 0; i < pixels.Length; ++i)
      pixels[i] = (byte)(i * 17 + 3);

    var palette = new byte[HalfLifeModelFile.PaletteEntries * 3];
    for (var i = 0; i < HalfLifeModelFile.PaletteEntries; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)(i * 7);
      palette[i * 3 + 2] = (byte)(i * 13);
    }

    return new() {
      Width = width,
      Height = height,
      SkinCount = 1,
      Name = name,
      PixelData = pixels,
      Palette = palette,
    };
  }
}
