using System;
using FileFormat.Core;

namespace FileFormat.MsxScreen2.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  /// <summary>Each 8x8 cell is solid one of four exact TMS9918 colours, cycling by cell index — well
  /// inside the two-colours-per-row budget, so quantization and row selection are both exact.</summary>
  private static RawImage _SolidCells() {
    const int width = MsxScreen2File.FixedWidth, height = MsxScreen2File.FixedHeight;
    var palette = MsxGraphics.Tms9918Palette;
    ReadOnlySpan<int> colorSlots = [1, 4, 8, 12];
    var data = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var cellIndex = (y / 8) * (width / 8) + x / 8;
      var slot = colorSlots[cellIndex % 4];
      var o = (y * width + x) * 4;
      data[o] = palette[slot * 3 + 2];
      data[o + 1] = palette[slot * 3 + 1];
      data[o + 2] = palette[slot * 3];
      data[o + 3] = 255;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_SolidCells_ReproducesExactly() {
    var source = _SolidCells();
    var file = MsxScreen2File.FromRawImage(source);
    var restored = MsxScreen2Reader.FromBytes(MsxScreen2Writer.ToBytes(file));
    var decoded = MsxScreen2File.ToRawImage(restored);
    var decodedBgra = PixelConverter.Convert(decoded, PixelFormat.Bgra32);

    Assert.That(decodedBgra.PixelData, Is.EqualTo(source.PixelData));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_RejectsWrongDimensions() {
    var raw = new RawImage { Width = 100, Height = 100, Format = PixelFormat.Rgb24, PixelData = new byte[100 * 100 * 3] };

    Assert.Throws<ArgumentException>(() => MsxScreen2File.FromRawImage(raw));
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_EveryCellAddressesItsOwnPatternSlot() {
    var file = MsxScreen2File.FromRawImage(_SolidCells());

    // The name table must contain no duplicate (bank, charIndex) pairs: 32 columns times the low
    // three bits of the row give 256 distinct slots per bank, one per cell in that bank.
    for (var bank = 0; bank < 3; ++bank) {
      var seen = new bool[256];
      for (var row = bank * 8; row < bank * 8 + 8; ++row)
      for (var col = 0; col < 32; ++col) {
        var charIndex = file.PatternNameTable[row * 32 + col];
        Assert.That(seen[charIndex], Is.False, $"duplicate slot in bank {bank} at ({row},{col})");
        seen[charIndex] = true;
      }
    }
  }
}
