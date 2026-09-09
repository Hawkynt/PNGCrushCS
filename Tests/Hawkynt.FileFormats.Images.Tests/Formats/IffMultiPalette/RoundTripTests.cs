using System;
using System.Buffers.Binary;
using System.IO;
using FileFormat.Core;
using FileFormat.Ilbm;
using FileFormat.IffMultiPalette;

namespace FileFormat.IffMultiPalette.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RepresentablePaletteChanges_RoundTripPixelExactly() {
    var source = _CreateRepresentablePicture();

    var encoded = IffMultiPaletteFile.FromRawImage(source);
    var bytes = IffMultiPaletteWriter.ToBytes(encoded);
    var restored = IffMultiPaletteReader.FromBytes(bytes);
    var decoded = IffMultiPaletteFile.ToRawImage(restored);

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(source.Width));
      Assert.That(decoded.Height, Is.EqualTo(source.Height));
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void WrittenPchg_DecodesThroughGeneralIlbmReader() {
    var source = _CreateRepresentablePicture();
    var file = IffMultiPaletteFile.FromRawImage(source);
    var bytes = IffMultiPaletteWriter.ToBytes(file);

    var ilbm = IlbmReader.FromBytes(bytes);
    var decoded = IlbmFile.ToRawImage(ilbm);

    Assert.Multiple(() => {
      Assert.That(ilbm.ScanlinePalettes, Is.Not.Null);
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Integration")]
  public void Writer_EmitsDocumentedIlbmPchgLayout() {
    var file = IffMultiPaletteFile.FromRawImage(_CreateRepresentablePicture());
    var bytes = IffMultiPaletteWriter.ToBytes(file);

    Assert.Multiple(() => {
      Assert.That(bytes.AsSpan(0, 4).SequenceEqual("FORM"u8), Is.True);
      Assert.That(bytes.AsSpan(8, 4).SequenceEqual("ILBM"u8), Is.True);
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4)), Is.EqualTo((uint)bytes.Length - 8));
    });

    var pchg = _GetChunk(bytes, "PCHG"u8);
    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(pchg), Is.Zero, "writer uses uncompressed PCHG");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(pchg[2..]) & 1, Is.EqualTo(1), "small twelve-bit PCHG form");
      Assert.That(BinaryPrimitives.ReadInt16BigEndian(pchg[4..]), Is.EqualTo(1), "CMAP supplies line zero");
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(pchg[6..]), Is.EqualTo(file.Height - 1));
      Assert.That(BinaryPrimitives.ReadUInt32BigEndian(pchg[16..]), Is.EqualTo((uint)pchg.Length - 20));
    });

    var lineCount = BinaryPrimitives.ReadUInt16BigEndian(pchg[6..]);
    var maskBytes = (lineCount + 31) / 32 * 4;
    var at = 20 + maskBytes;
    for (var line = 0; line < lineCount; ++line) {
      if ((pchg[20 + (line >> 3)] & 1 << (7 - (line & 7))) == 0)
        continue;

      var small = pchg[at];
      var big = pchg[at + 1];
      Assert.Multiple(() => {
        Assert.That(small, Is.LessThanOrEqualTo(IffMultiPaletteFile.MaxChangesPerLine));
        Assert.That(big, Is.Zero);
      });
      at += 2 + (small + big) * 2;
    }

    Assert.That(at, Is.EqualTo(pchg.Length));
  }

  [Test]
  [Category("Integration")]
  public void OneLinePicture_StillCarriesPchgMarkerAndRoundTrips() {
    var source = _CreatePicture([[0x123, 0x456, 0x789, 0xABC]]);

    var bytes = IffMultiPaletteWriter.ToBytes(IffMultiPaletteFile.FromRawImage(source));
    var pchg = _GetChunk(bytes, "PCHG"u8);
    var decoded = IffMultiPaletteFile.ToRawImage(IffMultiPaletteReader.FromBytes(bytes));

    Assert.Multiple(() => {
      Assert.That(BinaryPrimitives.ReadUInt16BigEndian(pchg[6..]), Is.Zero);
      Assert.That(pchg.Length, Is.EqualTo(20));
      Assert.That(decoded.PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void Writer_RefusesMoreThanSevenRegisterChangesOnOneLine() {
    var palettes = new byte[IffMultiPaletteFile.PaletteByteSize * 2];
    for (var register = 0; register < 8; ++register)
      palettes[IffMultiPaletteFile.PaletteByteSize + register * 3] = (byte)((register + 1) * 17);

    var file = new IffMultiPaletteFile {
      Width = 1,
      Height = 2,
      PixelData = [0, 0],
      ScanlinePalettes = palettes,
    };

    var exception = Assert.Throws<NotSupportedException>(() => IffMultiPaletteWriter.ToBytes(file));
    Assert.That(exception!.Message, Does.Contain("at most 7"));
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_NeverUsesMoreThanSevenChangesAfterFirstLine() {
    const int width = 128;
    const int height = 64;
    var rgb = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var at = (y * width + x) * 3;
      rgb[at] = (byte)(x * 31 + y * 17);
      rgb[at + 1] = (byte)(x * 11 + y * 47);
      rgb[at + 2] = (byte)(x * 73 + y * 5);
    }

    var file = IffMultiPaletteFile.FromRawImage(new() {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = rgb,
    });

    for (var y = 1; y < height; ++y) {
      var previous = (y - 1) * IffMultiPaletteFile.PaletteByteSize;
      var current = y * IffMultiPaletteFile.PaletteByteSize;
      var changed = 0;
      for (var register = 0; register < IffMultiPaletteFile.PaletteEntries; ++register)
        if (!file.ScanlinePalettes.AsSpan(previous + register * 3, 3).SequenceEqual(file.ScanlinePalettes.AsSpan(current + register * 3, 3)))
          ++changed;

      Assert.That(changed, Is.LessThanOrEqualTo(IffMultiPaletteFile.MaxChangesPerLine), $"scanline {y}");
    }
  }

  private static RawImage _CreateRepresentablePicture()
    => _CreatePicture([
      [0x000, 0x100, 0x200, 0x300, 0x400, 0x500, 0x600, 0x700, 0x800, 0x900, 0xA00, 0xB00, 0xC00, 0xD00, 0xE00, 0xF00],
      [0x000, 0x100, 0x200, 0x300, 0x400, 0x500, 0x600, 0x700, 0x800, 0x0F0, 0x1F0, 0x2F0, 0x3F0, 0x4F0, 0x5F0, 0x6F0],
      [0x00F, 0x10F, 0x20F, 0x30F, 0x40F, 0x50F, 0x60F, 0x700, 0x800, 0x0F0, 0x1F0, 0x2F0, 0x3F0, 0x4F0, 0x5F0, 0x6F0],
    ]);

  private static RawImage _CreatePicture(int[][] lines) {
    var width = lines[0].Length;
    var data = new byte[width * lines.Length * 3];
    for (var y = 0; y < lines.Length; ++y) {
      Assert.That(lines[y], Has.Length.EqualTo(width));
      for (var x = 0; x < width; ++x) {
        var colour = lines[y][x];
        var at = (y * width + x) * 3;
        data[at] = (byte)((colour >> 8 & 15) * 17);
        data[at + 1] = (byte)((colour >> 4 & 15) * 17);
        data[at + 2] = (byte)((colour & 15) * 17);
      }
    }

    return new() {
      Width = width,
      Height = lines.Length,
      Format = PixelFormat.Rgb24,
      PixelData = data,
    };
  }

  private static ReadOnlySpan<byte> _GetChunk(byte[] bytes, ReadOnlySpan<byte> wanted) {
    var end = Math.Min(bytes.Length, checked(8 + (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4))));
    for (var offset = 12; offset + 8 <= end;) {
      var size = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset + 4)));
      if (bytes.AsSpan(offset, 4).SequenceEqual(wanted))
        return bytes.AsSpan(offset + 8, size);
      offset = checked(offset + 8 + size + (size & 1));
    }

    throw new AssertionException($"Chunk {System.Text.Encoding.ASCII.GetString(wanted)} was not written.");
  }
}
