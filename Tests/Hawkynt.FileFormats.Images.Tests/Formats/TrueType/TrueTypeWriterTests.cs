using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FileFormat.Core;
using FileFormat.TrueType;

namespace FileFormat.TrueType.Tests;

[TestFixture]
public sealed class TrueTypeWriterTests {

  private static TrueTypeFile _OutlineFont() => new() {
    UnitsPerEm = 1000,
    GlyphCount = 2,
    Glyphs = [
      new TrueTypeGlyph([
        new TrueTypePoint[] {
          new(-120, -80, true),
          new(720, -80, true),
          new(720, 840, true),
          new(-120, 840, true),
        },
      ]),
      new TrueTypeGlyph([
        new TrueTypePoint[] {
          new(0, 0, true),
          new(250, 700, false),
          new(500, 700, false),
          new(750, 0, true),
        },
      ]),
    ],
  };

  [Test]
  [Category("Unit")]
  public void ToBytes_RoundTripsTheOutlineModel() {
    var source = _OutlineFont();

    var restored = TrueTypeReader.FromBytes(TrueTypeWriter.ToBytes(source));

    Assert.Multiple(() => {
      Assert.That(restored.UnitsPerEm, Is.EqualTo(source.UnitsPerEm));
      Assert.That(restored.GlyphCount, Is.EqualTo(source.GlyphCount));
      Assert.That(restored.Glyphs[0].Contours[0], Is.EqualTo(source.Glyphs[0].Contours[0]));
      Assert.That(restored.Glyphs[1].Contours[0], Is.EqualTo(source.Glyphs[1].Contours[0]));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesEveryRequiredOpenTypeTable() {
    var bytes = TrueTypeWriter.ToBytes(_OutlineFont());
    var tags = _Directory(bytes).Keys;

    Assert.That(tags, Is.SupersetOf(new[] { "OS/2", "cmap", "glyf", "head", "hhea", "hmtx", "loca", "maxp", "name", "post" }));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_WritesValidTableAndWholeFontChecksums() {
    var bytes = TrueTypeWriter.ToBytes(_OutlineFont());
    var directory = _Directory(bytes);

    Assert.That(_Checksum(bytes), Is.EqualTo(0xB1B0AFBAu), "head.checkSumAdjustment must make the complete sfnt sum to the OpenType checksum constant");

    foreach (var (tag, table) in directory) {
      var data = bytes.AsSpan(table.Offset, table.Length).ToArray();
      if (tag == "head")
        data.AsSpan(8, 4).Clear();

      Assert.That(_Checksum(data), Is.EqualTo(table.Checksum), $"{tag} table checksum");
    }
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_CmapMapsGlyphsAfterNotdefIntoThePrivateUseArea() {
    var bytes = TrueTypeWriter.ToBytes(_OutlineFont());
    var cmap = _Directory(bytes)["cmap"];
    var table = bytes.AsSpan(cmap.Offset, cmap.Length);

    Assert.Multiple(() => {
      Assert.That(_U16(table, 0), Is.Zero);
      Assert.That(_U16(table, 2), Is.EqualTo(2));
    });

    var subtable = checked((int)_U32(table, 8));
    Assert.Multiple(() => {
      Assert.That(_U16(table, subtable), Is.EqualTo(4));
      Assert.That(_MapFormat4(table.Slice(subtable), 0xE000), Is.EqualTo(1));
      Assert.That(_MapFormat4(table.Slice(subtable), 0xE001), Is.EqualTo(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_AFractionalCompositeResultIsRoundedDeterministically() {
    var source = new TrueTypeFile {
      UnitsPerEm = 1000,
      GlyphCount = 1,
      Glyphs = [new TrueTypeGlyph([
        new TrueTypePoint[] {
          new(0.49, -0.49, true),
          new(100.5, 0, false),
          new(200.51, 100.5, true),
        },
      ])],
    };

    var restored = TrueTypeReader.FromBytes(TrueTypeWriter.ToBytes(source));

    Assert.That(restored.Glyphs[0].Contours[0], Is.EqualTo(new[] {
      new TrueTypePoint(0, 0, true),
      new TrueTypePoint(101, 0, false),
      new TrueTypePoint(201, 101, true),
    }));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_MismatchedGlyphCountIsRefused() {
    var source = _OutlineFont() with { GlyphCount = 1 };

    Assert.That(() => TrueTypeWriter.ToBytes(source), Throws.ArgumentException.With.Message.Contains("GlyphCount"));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_CoalescesDarkHorizontalRunsIntoContours() {
    var pixels = Enumerable.Repeat((byte)255, 4 * 2 * 3).ToArray();
    _Black(pixels, 4, 1, 0);
    _Black(pixels, 4, 2, 0);
    var image = new RawImage { Width = 4, Height = 2, Format = PixelFormat.Rgb24, PixelData = pixels };

    var font = TrueTypeFile.FromRawImage(image);
    var restored = TrueTypeReader.FromBytes(TrueTypeWriter.ToBytes(font));

    Assert.Multiple(() => {
      Assert.That(font.UnitsPerEm, Is.EqualTo(16));
      Assert.That(restored.GlyphCount, Is.EqualTo(1));
      Assert.That(restored.Glyphs[0].Contours, Has.Count.EqualTo(1));
      Assert.That(restored.Glyphs[0].Contours[0], Is.EqualTo(new[] {
        new TrueTypePoint(1, 2, true),
        new TrueTypePoint(1, 1, true),
        new TrueTypePoint(3, 1, true),
        new TrueTypePoint(3, 2, true),
      }));
    });
  }

  private static void _Black(byte[] pixels, int width, int x, int y) {
    var at = (y * width + x) * 3;
    pixels[at] = pixels[at + 1] = pixels[at + 2] = 0;
  }

  private static Dictionary<string, (uint Checksum, int Offset, int Length)> _Directory(byte[] bytes) {
    var count = _U16(bytes, 4);
    var result = new Dictionary<string, (uint, int, int)>(StringComparer.Ordinal);
    for (var i = 0; i < count; ++i) {
      var at = 12 + i * 16;
      var tag = Encoding.ASCII.GetString(bytes, at, 4);
      result.Add(tag, (_U32(bytes, at + 4), checked((int)_U32(bytes, at + 8)), checked((int)_U32(bytes, at + 12))));
    }
    return result;
  }

  private static ushort _MapFormat4(ReadOnlySpan<byte> table, ushort codePoint) {
    var segmentCount = _U16(table, 6) / 2;
    var endCodes = 14;
    var startCodes = endCodes + segmentCount * 2 + 2;
    var deltas = startCodes + segmentCount * 2;
    var rangeOffsets = deltas + segmentCount * 2;

    for (var segment = 0; segment < segmentCount; ++segment) {
      var end = _U16(table, endCodes + segment * 2);
      if (codePoint > end)
        continue;

      var start = _U16(table, startCodes + segment * 2);
      if (codePoint < start)
        return 0;

      var rangeOffset = _U16(table, rangeOffsets + segment * 2);
      if (rangeOffset == 0)
        return unchecked((ushort)(codePoint + _U16(table, deltas + segment * 2)));

      var glyphOffset = rangeOffsets + segment * 2 + rangeOffset + (codePoint - start) * 2;
      var glyph = _U16(table, glyphOffset);
      return glyph == 0 ? (ushort)0 : unchecked((ushort)(glyph + _U16(table, deltas + segment * 2)));
    }

    return 0;
  }

  private static uint _Checksum(ReadOnlySpan<byte> data) {
    var sum = 0u;
    var at = 0;
    for (; at + 4 <= data.Length; at += 4)
      sum = unchecked(sum + _U32(data, at));

    if (at == data.Length)
      return sum;

    uint tail = 0;
    for (var shift = 24; at < data.Length; shift -= 8, ++at)
      tail |= (uint)data[at] << shift;
    return unchecked(sum + tail);
  }

  private static ushort _U16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
  private static uint _U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
}
