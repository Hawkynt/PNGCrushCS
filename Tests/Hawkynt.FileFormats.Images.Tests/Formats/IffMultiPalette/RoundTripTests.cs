using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.IffMultiPalette;

namespace FileFormat.IffMultiPalette.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Unit")]
  public void Writer_ProducesIlbmWithPchgBeforeBody() {
    var file = IffMultiPaletteFile.FromRawImage(_Rgb444Sample(16, 4));

    var bytes = IffMultiPaletteWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes.AsSpan(0, 4).SequenceEqual("FORM"u8), Is.True);
      Assert.That(bytes.AsSpan(8, 4).SequenceEqual("ILBM"u8), Is.True);
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4, 4)), Is.EqualTo((uint)(bytes.Length - 8)));
      Assert.That(_FindChunk(bytes, "PCHG"), Is.GreaterThan(0));
      Assert.That(_FindChunk(bytes, "PCHG"), Is.LessThan(_FindChunk(bytes, "BODY")));
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_OneRegisterChange_HasKnownPchgLayout() {
    var palettes = new byte[3 * IffMultiPaletteFile.PaletteBytes];
    palettes[1 * IffMultiPaletteFile.PaletteBytes + 2 * 3] = 0xFF;
    palettes[2 * IffMultiPaletteFile.PaletteBytes + 2 * 3] = 0xFF;

    var file = new IffMultiPaletteFile {
      Width = 16,
      Height = 3,
      NumPlanes = 4,
      PixelData = new byte[48],
      Palette = new byte[IffMultiPaletteFile.PaletteBytes],
      ScanlinePalettes = palettes,
      RawData = [],
    };

    var bytes = IffMultiPaletteWriter.ToBytes(file);
    var pchg = _ChunkData(bytes, "PCHG");

    Assert.Multiple(() => {
      Assert.That(pchg, Has.Length.EqualTo(28));
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(pchg), Is.Zero, "compression");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(pchg.AsSpan(2)), Is.EqualTo(1), "PCHGF_4BIT");
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(pchg.AsSpan(4)), Is.EqualTo(1), "start line");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(pchg.AsSpan(6)), Is.EqualTo(2), "line count");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(pchg.AsSpan(8)), Is.EqualTo(2), "minimum changed register");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(pchg.AsSpan(10)), Is.EqualTo(2), "maximum changed register");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(pchg.AsSpan(12)), Is.Zero, "Huffman tree size");
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(pchg.AsSpan(16)), Is.EqualTo(8), "uncompressed payload size");
      Assert.That(pchg.AsSpan(20, 4).ToArray(), Is.EqualTo(new byte[] { 0x80, 0, 0, 0 }), "line mask");
      Assert.That(pchg.AsSpan(24, 4).ToArray(), Is.EqualTo(new byte[] { 1, 0, 0x2F, 0x00 }), "one RGB444 write to register 2");
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_MoreThanSevenChangesOnOneLine_IsRefused() {
    var palettes = new byte[2 * IffMultiPaletteFile.PaletteBytes];
    for (var register = 0; register < IffMultiPaletteFile.MaxChangesPerLine + 1; ++register)
      palettes[IffMultiPaletteFile.PaletteBytes + register * 3] = (byte)((register + 1) * 0x11);

    var file = new IffMultiPaletteFile {
      Width = 16,
      Height = 2,
      NumPlanes = 4,
      PixelData = new byte[32],
      Palette = new byte[IffMultiPaletteFile.PaletteBytes],
      ScanlinePalettes = palettes,
      RawData = [],
    };

    var error = Assert.Throws<InvalidDataException>(() => IffMultiPaletteWriter.ToBytes(file));
    Assert.That(error!.Message, Does.Contain("at most 7"));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_NeverSchedulesMoreThanSevenWritesPerLine() {
    const int width = 64;
    const int height = 12;
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      pixels[at] = (byte)(x * 4);
      pixels[at + 1] = (byte)(y * 21);
      pixels[at + 2] = (byte)((x * 13 + y * 29) & 0xFF);
    }

    var file = IffMultiPaletteFile.FromRawImage(new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    });

    for (var y = 1; y < height; ++y) {
      var changed = 0;
      for (var register = 0; register < IffMultiPaletteFile.PaletteEntries; ++register) {
        var previous = (y - 1) * IffMultiPaletteFile.PaletteBytes + register * 3;
        var current = y * IffMultiPaletteFile.PaletteBytes + register * 3;
        if (!file.ScanlinePalettes.AsSpan(previous, 3).SequenceEqual(file.ScanlinePalettes.AsSpan(current, 3)))
          ++changed;
      }

      Assert.That(changed, Is.LessThanOrEqualTo(IffMultiPaletteFile.MaxChangesPerLine), $"line {y}");
    }

    Assert.DoesNotThrow(() => IffMultiPaletteWriter.ToBytes(file));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Rgb444PictureWithinSixteenColours_IsExact() {
    var source = _Rgb444Sample(32, 8);

    var file = IffMultiPaletteFile.FromRawImage(source);
    var bytes = IffMultiPaletteWriter.ToBytes(file);
    var restored = IffMultiPaletteReader.FromBytes(bytes);
    var actual = IffMultiPaletteFile.ToRawImage(restored);

    Assert.Multiple(() => {
      Assert.That(actual.Width, Is.EqualTo(source.Width));
      Assert.That(actual.Height, Is.EqualTo(source.Height));
      Assert.That(actual.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(actual.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ArbitraryTrueColourImage_PreservesGeometryAndRenderablePixels() {
    const int width = 37;
    const int height = 11;
    var pixels = new byte[width * height * 4];
    for (var i = 0; i < width * height; ++i) {
      pixels[i * 4] = (byte)(i * 17);
      pixels[i * 4 + 1] = (byte)(i * 31);
      pixels[i * 4 + 2] = (byte)(i * 47);
      pixels[i * 4 + 3] = 255;
    }

    var source = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgba32,
      PixelData = pixels,
    };

    var bytes = IffMultiPaletteWriter.ToBytes(IffMultiPaletteFile.FromRawImage(source));
    var restored = IffMultiPaletteReader.FromBytes(bytes);
    var actual = IffMultiPaletteFile.ToRawImage(restored);

    Assert.Multiple(() => {
      Assert.That((restored.Width, restored.Height), Is.EqualTo((width, height)));
      Assert.That(actual.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(actual.PixelData, Has.Length.EqualTo(width * height * 3));
    });
  }

  private static RawImage _Rgb444Sample(int width, int height) {
    var colours = new (byte R, byte G, byte B)[] {
      (0x00, 0x00, 0x00), (0xFF, 0x00, 0x00), (0x00, 0xFF, 0x00), (0x00, 0x00, 0xFF),
      (0xFF, 0xFF, 0x00), (0x00, 0xFF, 0xFF), (0xFF, 0x00, 0xFF), (0x88, 0x88, 0x88),
    };
    var pixels = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var colour = colours[(x + y * 3) % colours.Length];
      var at = (y * width + x) * 3;
      pixels[at] = colour.R;
      pixels[at + 1] = colour.G;
      pixels[at + 2] = colour.B;
    }

    return new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = pixels,
    };
  }

  private static int _FindChunk(byte[] data, string id) {
    var offset = 12;
    while (offset + 8 <= data.Length) {
      var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + 4)));
      if (data.AsSpan(offset, 4).SequenceEqual(System.Text.Encoding.ASCII.GetBytes(id)))
        return offset;
      offset = checked(offset + 8 + length + (length & 1));
    }
    return -1;
  }

  private static byte[] _ChunkData(byte[] data, string id) {
    var offset = _FindChunk(data, id);
    Assert.That(offset, Is.GreaterThanOrEqualTo(0), $"missing {id} chunk");
    var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset + 4)));
    return data.AsSpan(offset + 8, length).ToArray();
  }
}
