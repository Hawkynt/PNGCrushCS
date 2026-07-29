using System;
using System.IO;
using FileFormat.Core;
using FileFormat.McPainter;

namespace FileFormat.McPainter.Tests;

[TestFixture]
public sealed class McPainterTests {

  /// <summary>A file whose two fields and two register sets are all given distinct values.</summary>
  private static byte[] _Probe() {
    var data = new byte[McPainterFile.FileSize];
    for (var i = 0; i < McPainterFile.RegistersPerSet; ++i) {
      data[McPainterFile.ColorsOffset + i] = (byte)(0x10 + i * 0x20);
      data[McPainterFile.ColorsOffset + McPainterFile.RegistersPerSet + i] = (byte)(0x90 + i * 0x20);
    }

    return data;
  }

  private static (byte R, byte G, byte B) _PixelAt(RawImage image, int x, int y) {
    var o = (y * image.Width + x) * 3;
    return (image.PixelData[o], image.PixelData[o + 1], image.PixelData[o + 2]);
  }

  private static (byte R, byte G, byte B) _Color(byte value) {
    var p = Atari8BitGraphics.Palette;
    var i = (value & 254) * 3;
    return (p[i], p[i + 1], p[i + 2]);
  }

  private static (byte R, byte G, byte B) _Blend(byte first, byte second) {
    var (r1, g1, b1) = _Color(first);
    var (r2, g2, b2) = _Color(second);
    static byte Mix(byte a, byte b) => (byte)((a & b) + (((a ^ b) >> 1) & 0x7F));
    return (Mix(r1, r2), Mix(g1, g2), Mix(b1, b2));
  }

  [Test]
  public void Dimensions_AreTheDisplayedOnes() {
    var image = McPainterFile.ToRawImage(McPainterReader.FromBytes(_Probe()));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(320));
      Assert.That(image.Height, Is.EqualTo(200));
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
    });
  }

  [Test]
  public void AnEmptyBitmap_BlendsTheTwoBackgroundRegisters() {
    // Pixel value 0 is the background in both fields, and the two fields use different sets.
    var data = _Probe();
    var image = McPainterFile.ToRawImage(McPainterReader.FromBytes(data));

    var setA = data[McPainterFile.ColorsOffset + 3];
    var setB = data[McPainterFile.ColorsOffset + McPainterFile.RegistersPerSet + 3];

    Assert.That(_PixelAt(image, 0, 0), Is.EqualTo(_Blend(setA, setB)));
  }

  [Test]
  public void RegisterSets_SwapBetweenAlternateScanlines() {
    // Both fields put set A on one parity and set B on the other, so with an empty bitmap every
    // scanline blends the same pair — a decoder that failed to swap would still agree here, which
    // is why the next test drives the two fields apart instead.
    var image = McPainterFile.ToRawImage(McPainterReader.FromBytes(_Probe()));

    Assert.That(_PixelAt(image, 0, 1), Is.EqualTo(_PixelAt(image, 0, 0)));
  }

  [Test]
  public void TheTwoFieldsAreBlended_NotJustTheFirst() {
    var data = _Probe();
    // Light pixel 0 of scanline 0 in the second field only: it takes PF0 there and the background
    // in the first, so the result is a blend of two different registers.
    data[McPainterFile.SecondFieldOffset] = 0b0100_0000;

    var image = McPainterFile.ToRawImage(McPainterReader.FromBytes(data));
    var firstFieldBackground = data[McPainterFile.ColorsOffset + 3];
    var secondFieldPf0 = data[McPainterFile.ColorsOffset + McPainterFile.RegistersPerSet];

    Assert.That(_PixelAt(image, 0, 0), Is.EqualTo(_Blend(firstFieldBackground, secondFieldPf0)));
  }

  [Test]
  public void PixelValues_RotateOntoTheRegisters() {
    var data = _Probe();
    // Scanline 0 of both fields: pixel values 0, 1, 2, 3 across the first byte.
    data[0] = data[McPainterFile.SecondFieldOffset] = 0b00_01_10_11;

    var image = McPainterFile.ToRawImage(McPainterReader.FromBytes(data));
    var a = McPainterFile.ColorsOffset;
    var b = a + McPainterFile.RegistersPerSet;

    Assert.Multiple(() => {
      // 0 is the background, 1..3 are PF0..PF2 — a rotation, not the identity.
      Assert.That(_PixelAt(image, 0, 0), Is.EqualTo(_Blend(data[a + 3], data[b + 3])), "value 0");
      Assert.That(_PixelAt(image, 2, 0), Is.EqualTo(_Blend(data[a], data[b])), "value 1");
      Assert.That(_PixelAt(image, 4, 0), Is.EqualTo(_Blend(data[a + 1], data[b + 1])), "value 2");
      Assert.That(_PixelAt(image, 6, 0), Is.EqualTo(_Blend(data[a + 2], data[b + 2])), "value 3");
    });
  }

  [Test]
  public void EachLogicalPixel_CoversTwoScreenPixels() {
    var data = _Probe();
    data[0] = data[McPainterFile.SecondFieldOffset] = 0b11_00_00_00;

    var image = McPainterFile.ToRawImage(McPainterReader.FromBytes(data));

    Assert.Multiple(() => {
      Assert.That(_PixelAt(image, 1, 0), Is.EqualTo(_PixelAt(image, 0, 0)));
      Assert.That(_PixelAt(image, 2, 0), Is.Not.EqualTo(_PixelAt(image, 0, 0)));
    });
  }

  [Test]
  public void ColorRegisters_IgnoreTheirLowBit() {
    var data = _Probe();
    var odd = _Probe();
    for (var i = 0; i < McPainterFile.RegistersPerSet * 2; ++i)
      odd[McPainterFile.ColorsOffset + i] |= 1;

    Assert.That(
      McPainterFile.ToRawImage(McPainterReader.FromBytes(odd)).PixelData,
      Is.EqualTo(McPainterFile.ToRawImage(McPainterReader.FromBytes(data)).PixelData));
  }

  [Test]
  public void Reader_RejectsAnyOtherLength() {
    Assert.Multiple(() => {
      Assert.Throws<InvalidDataException>(() => McPainterReader.FromBytes(new byte[McPainterFile.FileSize - 1]));
      Assert.Throws<InvalidDataException>(() => McPainterReader.FromBytes(new byte[McPainterFile.FileSize + 1]));
    });
  }
}
