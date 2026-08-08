using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using FileFormat.Core;
using FileFormat.Tiff;

namespace FileFormat.Tiff.Tests;

/// <summary>
/// A TIFF whose samples are narrower than a byte.
/// </summary>
/// <remarks>
/// Four bits with a colour map is an ordinary way to store a small picture — it is what every
/// Neopaint stationery file is — and it was refused outright, so those files could not be opened at
/// all. The palette itself was a second fault: the reader builds RGB triplets and the converter in
/// front of it read them as three sixteen-bit planes, which dropped a third of the entries and took
/// a green byte for a red. The files here are assembled byte by byte rather than through this
/// project's own writer, so a mistake shared by the two could not hide in them.
/// </remarks>
[TestFixture]
public sealed class TiffShallowDepthTests {

  private const ushort _TAG_IMAGE_WIDTH = 256;
  private const ushort _TAG_IMAGE_LENGTH = 257;
  private const ushort _TAG_BITS_PER_SAMPLE = 258;
  private const ushort _TAG_COMPRESSION = 259;
  private const ushort _TAG_PHOTOMETRIC = 262;
  private const ushort _TAG_STRIP_OFFSETS = 273;
  private const ushort _TAG_SAMPLES_PER_PIXEL = 277;
  private const ushort _TAG_ROWS_PER_STRIP = 278;
  private const ushort _TAG_STRIP_BYTE_COUNTS = 279;
  private const ushort _TAG_COLOR_MAP = 320;

  private const ushort _TYPE_SHORT = 3;
  private const ushort _TYPE_LONG = 4;

  /// <summary>Builds an uncompressed palette TIFF at the stated depth, laid out by hand.</summary>
  private static byte[] _BuildPaletteTiff(int width, int height, int bitsPerSample, byte[] palette, byte[] rows) {
    var entryCount = 1 << bitsPerSample;
    var colorMap = new byte[entryCount * 3 * 2];
    for (var i = 0; i < entryCount; ++i) {
      BinaryPrimitives.WriteUInt16LittleEndian(colorMap.AsSpan(i * 2), (ushort)(palette[i * 3] * 257));
      BinaryPrimitives.WriteUInt16LittleEndian(colorMap.AsSpan((entryCount + i) * 2), (ushort)(palette[i * 3 + 1] * 257));
      BinaryPrimitives.WriteUInt16LittleEndian(colorMap.AsSpan((entryCount * 2 + i) * 2), (ushort)(palette[i * 3 + 2] * 257));
    }

    var tags = new List<(ushort Tag, ushort Type, uint Count, uint Value, byte[]? External)> {
      (_TAG_IMAGE_WIDTH, _TYPE_LONG, 1, (uint)width, null),
      (_TAG_IMAGE_LENGTH, _TYPE_LONG, 1, (uint)height, null),
      (_TAG_BITS_PER_SAMPLE, _TYPE_SHORT, 1, (uint)bitsPerSample, null),
      (_TAG_COMPRESSION, _TYPE_SHORT, 1, 1, null),
      (_TAG_PHOTOMETRIC, _TYPE_SHORT, 1, 3, null),
      (_TAG_STRIP_OFFSETS, _TYPE_LONG, 1, 0, null),
      (_TAG_SAMPLES_PER_PIXEL, _TYPE_SHORT, 1, 1, null),
      (_TAG_ROWS_PER_STRIP, _TYPE_LONG, 1, (uint)height, null),
      (_TAG_STRIP_BYTE_COUNTS, _TYPE_LONG, 1, (uint)rows.Length, null),
      (_TAG_COLOR_MAP, _TYPE_SHORT, (uint)(entryCount * 3), 0, colorMap),
    };

    var directoryOffset = 8;
    var afterDirectory = directoryOffset + 2 + tags.Count * 12 + 4;
    var externalOffset = afterDirectory;
    for (var i = 0; i < tags.Count; ++i)
      if (tags[i].External != null) {
        tags[i] = (tags[i].Tag, tags[i].Type, tags[i].Count, (uint)externalOffset, tags[i].External);
        externalOffset += tags[i].External!.Length;
      }

    var pixelOffset = externalOffset;
    for (var i = 0; i < tags.Count; ++i)
      if (tags[i].Tag == _TAG_STRIP_OFFSETS)
        tags[i] = (tags[i].Tag, tags[i].Type, tags[i].Count, (uint)pixelOffset, null);

    var file = new byte[pixelOffset + rows.Length];
    file[0] = 0x49;
    file[1] = 0x49;
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2), 42);
    BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), (uint)directoryOffset);
    BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(directoryOffset), (ushort)tags.Count);

    var at = directoryOffset + 2;
    foreach (var (tag, type, count, value, external) in tags) {
      BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(at), tag);
      BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(at + 2), type);
      BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 4), count);
      if (type == _TYPE_SHORT && count == 1)
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(at + 8), (ushort)value);
      else
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(at + 8), value);

      external?.CopyTo(file.AsSpan((int)value));
      at += 12;
    }

    rows.CopyTo(file.AsSpan(pixelOffset));
    return file;
  }

  [Test]
  [Category("Unit")]
  public void FourBitPalette_DrawsEveryPixel() {
    var palette = new byte[16 * 3];
    for (var i = 0; i < 16; ++i) {
      palette[i * 3] = (byte)(i * 17);
      palette[i * 3 + 1] = (byte)(255 - i * 17);
      palette[i * 3 + 2] = (byte)(i * 3);
    }

    // Three pixels a row, so the row ends mid-byte and TIFF pads it: 0,1,2 then 3,4,5.
    byte[] rows = [0x01, 0x20, 0x34, 0x50];
    var file = TiffReader.FromBytes(_BuildPaletteTiff(3, 2, 4, palette, rows));
    var image = TiffFile.ToRawImage(file).ToRgb24();

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(3));
      Assert.That(file.Height, Is.EqualTo(2));
      for (var i = 0; i < 6; ++i) {
        Assert.That(image[i * 3], Is.EqualTo(palette[i * 3]), $"red of pixel {i}");
        Assert.That(image[i * 3 + 1], Is.EqualTo(palette[i * 3 + 1]), $"green of pixel {i}");
        Assert.That(image[i * 3 + 2], Is.EqualTo(palette[i * 3 + 2]), $"blue of pixel {i}");
      }
    });
  }

  [Test]
  [Category("Unit")]
  public void EightBitPalette_KeepsEveryEntryAndItsOrder() {
    var palette = new byte[256 * 3];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)i;
      palette[i * 3 + 1] = (byte)(255 - i);
      palette[i * 3 + 2] = (byte)(i / 2);
    }

    byte[] rows = [0, 17, 200, 255];
    var file = TiffReader.FromBytes(_BuildPaletteTiff(4, 1, 8, palette, rows));
    var image = TiffFile.ToRawImage(file).ToRgb24();

    Assert.Multiple(() => {
      for (var i = 0; i < rows.Length; ++i) {
        var entry = rows[i];
        Assert.That(image[i * 3], Is.EqualTo(palette[entry * 3]), $"red of pixel {i}");
        Assert.That(image[i * 3 + 1], Is.EqualTo(palette[entry * 3 + 1]), $"green of pixel {i}");
        Assert.That(image[i * 3 + 2], Is.EqualTo(palette[entry * 3 + 2]), $"blue of pixel {i}");
      }
    });
  }

  [Test]
  [Category("Integration")]
  public void PaletteSurvivesBeingWrittenAndReadBack() {
    var palette = new byte[256 * 3];
    for (var i = 0; i < 256; ++i) {
      palette[i * 3] = (byte)((i * 7) & 0xFF);
      palette[i * 3 + 1] = (byte)((i * 13) & 0xFF);
      palette[i * 3 + 2] = (byte)((i * 29) & 0xFF);
    }

    var source = new RawImage {
      Width = 4,
      Height = 2,
      Format = PixelFormat.Indexed8,
      PixelData = [0, 1, 128, 255, 7, 60, 200, 42],
      Palette = palette,
      PaletteCount = 256,
    };

    var restored = TiffFile.ToRawImage(TiffReader.FromBytes(TiffWriter.ToBytes(TiffFile.FromRawImage(source))));
    Assert.That(restored.ToRgb24(), Is.EqualTo(source.ToRgb24()));
  }

  [Test]
  [Category("Unit")]
  public void NotATiff_IsRefused() {
    var data = new byte[64];
    data[0] = 0x4E;
    data[1] = 0x4F;
    Assert.That(() => TiffReader.FromBytes(data), Throws.Exception);
  }
}
