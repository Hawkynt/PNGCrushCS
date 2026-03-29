using System;
using FileFormat.Core;

namespace FileFormat.Core.Tests;

[TestFixture]
public sealed class PixelConverterTests {

  [Test]
  public void BgrToBgra_SinglePixel_SetsAlpha255() {
    byte[] bgr = [10, 20, 30];

    var result = PixelConverter.BgrToBgra(bgr, 1);

    Assert.That(result, Has.Length.EqualTo(4));
    Assert.That(result[0], Is.EqualTo(10));
    Assert.That(result[1], Is.EqualTo(20));
    Assert.That(result[2], Is.EqualTo(30));
    Assert.That(result[3], Is.EqualTo(255));
  }

  [Test]
  public void BgrToBgra_MultiplePixels_PreservesChannels() {
    byte[] bgr = [10, 20, 30, 40, 50, 60];

    var result = PixelConverter.BgrToBgra(bgr, 2);

    Assert.That(result, Has.Length.EqualTo(8));
    Assert.That(result[4], Is.EqualTo(40));
    Assert.That(result[5], Is.EqualTo(50));
    Assert.That(result[6], Is.EqualTo(60));
    Assert.That(result[7], Is.EqualTo(255));
  }

  [Test]
  public void RgbToBgra_SinglePixel_SwapsRedBlue() {
    byte[] rgb = [0xAA, 0xBB, 0xCC];

    var result = PixelConverter.RgbToBgra(rgb, 1);

    Assert.That(result[0], Is.EqualTo(0xCC));
    Assert.That(result[1], Is.EqualTo(0xBB));
    Assert.That(result[2], Is.EqualTo(0xAA));
    Assert.That(result[3], Is.EqualTo(255));
  }

  [Test]
  public void RgbaToBgra_SinglePixel_SwapsRedBluePreservesAlpha() {
    byte[] rgba = [0xAA, 0xBB, 0xCC, 0x80];

    var result = PixelConverter.RgbaToBgra(rgba, 1);

    Assert.That(result[0], Is.EqualTo(0xCC));
    Assert.That(result[1], Is.EqualTo(0xBB));
    Assert.That(result[2], Is.EqualTo(0xAA));
    Assert.That(result[3], Is.EqualTo(0x80));
  }

  [Test]
  public void Gray8ToBgra_SinglePixel_DuplicatesGrayWithAlpha255() {
    byte[] gray = [128];

    var result = PixelConverter.Gray8ToBgra(gray, 1);

    Assert.That(result[0], Is.EqualTo(128));
    Assert.That(result[1], Is.EqualTo(128));
    Assert.That(result[2], Is.EqualTo(128));
    Assert.That(result[3], Is.EqualTo(255));
  }

  [Test]
  public void IndexedToBgra_LooksPaletteByIndex() {
    byte[] indices = [1];
    byte[] palette = [0x10, 0x20, 0x30, 0x40, 0x50, 0x60];

    var result = PixelConverter.IndexedToBgra(indices, palette, 1);

    Assert.That(result[0], Is.EqualTo(0x60));
    Assert.That(result[1], Is.EqualTo(0x50));
    Assert.That(result[2], Is.EqualTo(0x40));
    Assert.That(result[3], Is.EqualTo(255));
  }

  [Test]
  public void IndexedToBgra_WithAlphaTable_UsesTableValue() {
    byte[] indices = [0];
    byte[] palette = [0xFF, 0x00, 0x00];
    byte[] alphaTable = [0x80];

    var result = PixelConverter.IndexedToBgra(indices, palette, 1, alphaTable);

    Assert.That(result[3], Is.EqualTo(0x80));
  }

  [Test]
  public void IndexedToBgra_IndexExceedsPalette_ClampedToMax() {
    byte[] indices = [200];
    byte[] palette = [0xFF, 0x00, 0x00];

    var result = PixelConverter.IndexedToBgra(indices, palette, 1);

    Assert.That(result[0], Is.EqualTo(0x00));
    Assert.That(result[2], Is.EqualTo(0xFF));
  }

  [Test]
  public void Rgba16BeToBgra_TakesHighBytesSwapsRedBlue() {
    byte[] rgba16 = [0xAA, 0x00, 0xBB, 0x00, 0xCC, 0x00, 0xDD, 0x00];

    var result = PixelConverter.Rgba16BeToBgra(rgba16, 1);

    Assert.That(result[0], Is.EqualTo(0xCC));
    Assert.That(result[1], Is.EqualTo(0xBB));
    Assert.That(result[2], Is.EqualTo(0xAA));
    Assert.That(result[3], Is.EqualTo(0xDD));
  }

  [Test]
  public void Rgb565ToBgra_KnownValue_ConvertsCorrectly() {
    var r = 31;
    var g = 63;
    var b = 0;
    var packed = (r << 11) | (g << 5) | b;
    byte[] rgb565 = [(byte)(packed & 0xFF), (byte)(packed >> 8)];

    var result = PixelConverter.Rgb565ToBgra(rgb565, 1);

    Assert.That(result[0], Is.EqualTo(0));
    Assert.That(result[1], Is.EqualTo(255));
    Assert.That(result[2], Is.EqualTo(255));
    Assert.That(result[3], Is.EqualTo(255));
  }

  [Test]
  public void ArgbToBgra_SinglePixel_ReordersChannels() {
    byte[] argb = [0xAA, 0xBB, 0xCC, 0xDD];

    var result = PixelConverter.ArgbToBgra(argb, 1);

    Assert.That(result[0], Is.EqualTo(0xDD));
    Assert.That(result[1], Is.EqualTo(0xCC));
    Assert.That(result[2], Is.EqualTo(0xBB));
    Assert.That(result[3], Is.EqualTo(0xAA));
  }

  [Test]
  public void BgraToRgb_DiscardAlphaSwapsRedBlue() {
    byte[] bgra = [0xCC, 0xBB, 0xAA, 0xFF];

    var result = PixelConverter.BgraToRgb(bgra, 1);

    Assert.That(result, Has.Length.EqualTo(3));
    Assert.That(result[0], Is.EqualTo(0xAA));
    Assert.That(result[1], Is.EqualTo(0xBB));
    Assert.That(result[2], Is.EqualTo(0xCC));
  }

  [Test]
  public void BgraToRgba_SwapsRedBluePreservesAlpha() {
    byte[] bgra = [0xCC, 0xBB, 0xAA, 0x80];

    var result = PixelConverter.BgraToRgba(bgra, 1);

    Assert.That(result[0], Is.EqualTo(0xAA));
    Assert.That(result[1], Is.EqualTo(0xBB));
    Assert.That(result[2], Is.EqualTo(0xCC));
    Assert.That(result[3], Is.EqualTo(0x80));
  }

  [Test]
  public void BgraToBgr_DiscardAlpha() {
    byte[] bgra = [0x10, 0x20, 0x30, 0xFF];

    var result = PixelConverter.BgraToBgr(bgra, 1);

    Assert.That(result, Has.Length.EqualTo(3));
    Assert.That(result[0], Is.EqualTo(0x10));
    Assert.That(result[1], Is.EqualTo(0x20));
    Assert.That(result[2], Is.EqualTo(0x30));
  }

  [Test]
  public void BgraToGray8_LuminanceFormula() {
    byte[] bgra = [0, 0, 255, 255];

    var result = PixelConverter.BgraToGray8(bgra, 1);

    var expected = (byte)((255 * 77 + 0 * 150 + 0 * 29) >> 8);
    Assert.That(result[0], Is.EqualTo(expected));
  }

  [Test]
  public void BgraToGray8_White_Is255() {
    byte[] bgra = [255, 255, 255, 255];

    var result = PixelConverter.BgraToGray8(bgra, 1);

    Assert.That(result[0], Is.EqualTo(255));
  }

  [Test]
  public void BgraToGray8_Black_Is0() {
    byte[] bgra = [0, 0, 0, 255];

    var result = PixelConverter.BgraToGray8(bgra, 1);

    Assert.That(result[0], Is.EqualTo(0));
  }

  [Test]
  public void BgraToArgb_SinglePixel_ReordersChannels() {
    byte[] bgra = [0xDD, 0xCC, 0xBB, 0xAA];

    var result = PixelConverter.BgraToArgb(bgra, 1);

    Assert.That(result[0], Is.EqualTo(0xAA));
    Assert.That(result[1], Is.EqualTo(0xBB));
    Assert.That(result[2], Is.EqualTo(0xCC));
    Assert.That(result[3], Is.EqualTo(0xDD));
  }

  [Test]
  public void BgraToRgb565_KnownWhite_ProducesMaxValue() {
    byte[] bgra = [255, 255, 255, 255];

    var result = PixelConverter.BgraToRgb565(bgra, 1);

    var value = result[0] | (result[1] << 8);
    var r = (value >> 11) & 0x1F;
    var g = (value >> 5) & 0x3F;
    var b = value & 0x1F;
    Assert.That(r, Is.EqualTo(31));
    Assert.That(g, Is.EqualTo(63));
    Assert.That(b, Is.EqualTo(31));
  }

  [Test]
  public void GrayAlpha16ToBgra_DuplicatesGrayPreservesAlpha() {
    byte[] ga = [200, 128];

    var result = PixelConverter.GrayAlpha16ToBgra(ga, 1);

    Assert.That(result[0], Is.EqualTo(200));
    Assert.That(result[1], Is.EqualTo(200));
    Assert.That(result[2], Is.EqualTo(200));
    Assert.That(result[3], Is.EqualTo(128));
  }

  [Test]
  public void Rgb48ToBgra_TakesHighBytesSwapsRedBlue() {
    byte[] rgb48 = [0xAA, 0x00, 0xBB, 0x00, 0xCC, 0x00];

    var result = PixelConverter.Rgb48ToBgra(rgb48, 1);

    Assert.That(result[0], Is.EqualTo(0xCC));
    Assert.That(result[1], Is.EqualTo(0xBB));
    Assert.That(result[2], Is.EqualTo(0xAA));
    Assert.That(result[3], Is.EqualTo(255));
  }

  [Test]
  public void Convert_SameFormat_ReturnsSameInstance() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = [0, 0, 0, 255],
    };

    var result = PixelConverter.Convert(image, PixelFormat.Bgra32);

    Assert.That(result, Is.SameAs(image));
  }

  [Test]
  public void Convert_NullSource_ThrowsArgumentNullException() {
    Assert.Throws<ArgumentNullException>(() => PixelConverter.Convert(null!, PixelFormat.Bgra32));
  }

  [Test]
  public void Convert_RgbToBgra_ReturnsCorrectFormat() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [0xAA, 0xBB, 0xCC],
    };

    var result = PixelConverter.Convert(image, PixelFormat.Bgra32);

    Assert.That(result.Format, Is.EqualTo(PixelFormat.Bgra32));
    Assert.That(result.Width, Is.EqualTo(1));
    Assert.That(result.Height, Is.EqualTo(1));
  }

  [Test]
  public void Convert_ViaIntermediate_RgbToGray8() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [255, 255, 255],
    };

    var result = PixelConverter.Convert(image, PixelFormat.Gray8);

    Assert.That(result.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(result.PixelData[0], Is.EqualTo(255));
  }

  [Test]
  public void RoundTrip_BgraThroughRgbaAndBack_PreservesPixels() {
    byte[] original = [0x10, 0x20, 0x30, 0x80];

    var rgba = PixelConverter.BgraToRgba(original, 1);
    var back = PixelConverter.RgbaToBgra(rgba, 1);

    Assert.That(back, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_BgraThroughArgbAndBack_PreservesPixels() {
    byte[] original = [0x10, 0x20, 0x30, 0x80];

    var argb = PixelConverter.BgraToArgb(original, 1);
    var back = PixelConverter.ArgbToBgra(argb, 1);

    Assert.That(back, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_Rgb565_PreservesQuantizedValues() {
    byte[] bgra = [0, 255, 255, 255];

    var rgb565 = PixelConverter.BgraToRgb565(bgra, 1);
    var back = PixelConverter.Rgb565ToBgra(rgb565, 1);

    Assert.That(back[2], Is.EqualTo(255));
    Assert.That(back[1], Is.EqualTo(255));
    Assert.That(back[0], Is.EqualTo(0));
    Assert.That(back[3], Is.EqualTo(255));
  }

  [Test]
  public void MultiplePixels_BgraToBgrAndBack_PreservesData() {
    byte[] bgra = [0x10, 0x20, 0x30, 0xFF, 0x40, 0x50, 0x60, 0xFF];

    var bgr = PixelConverter.BgraToBgr(bgra, 2);
    var back = PixelConverter.BgrToBgra(bgr, 2);

    Assert.That(back[0], Is.EqualTo(0x10));
    Assert.That(back[4], Is.EqualTo(0x40));
    Assert.That(back[7], Is.EqualTo(255));
  }

  [Test]
  public void Indexed4ToBgra_TwoPixels_UnpacksNibbles() {
    byte[] palette = [0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00];
    byte[] data = [0x10];

    var result = PixelConverter.Indexed4ToBgra(data, palette, 2);

    Assert.That(result, Has.Length.EqualTo(8));
    Assert.That(result[0], Is.EqualTo(0x00));
    Assert.That(result[1], Is.EqualTo(0xFF));
    Assert.That(result[2], Is.EqualTo(0x00));
    Assert.That(result[3], Is.EqualTo(255));
    Assert.That(result[4], Is.EqualTo(0x00));
    Assert.That(result[5], Is.EqualTo(0x00));
    Assert.That(result[6], Is.EqualTo(0xFF));
    Assert.That(result[7], Is.EqualTo(255));
  }

  [Test]
  public void Indexed4ToBgra_WithAlphaTable_UsesAlphaFromTable() {
    byte[] palette = [0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00];
    byte[] alphaTable = [0x40, 0x80];
    byte[] data = [0x10];

    var result = PixelConverter.Indexed4ToBgra(data, palette, 2, alphaTable);

    Assert.That(result[3], Is.EqualTo(0x80));
    Assert.That(result[7], Is.EqualTo(0x40));
  }

  [Test]
  public void Indexed1ToBgra_EightPixels_UnpacksBitsMsbFirst() {
    byte[] palette = [0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00];
    byte[] data = [0b10101010];

    var result = PixelConverter.Indexed1ToBgra(data, palette, 8);

    Assert.That(result, Has.Length.EqualTo(32));
    for (var i = 0; i < 8; ++i) {
      var dst = i * 4;
      if (i % 2 == 0) {
        Assert.That(result[dst], Is.EqualTo(0x00), $"Pixel {i} B");
        Assert.That(result[dst + 1], Is.EqualTo(0xFF), $"Pixel {i} G");
        Assert.That(result[dst + 2], Is.EqualTo(0x00), $"Pixel {i} R");
      } else {
        Assert.That(result[dst], Is.EqualTo(0x00), $"Pixel {i} B");
        Assert.That(result[dst + 1], Is.EqualTo(0x00), $"Pixel {i} G");
        Assert.That(result[dst + 2], Is.EqualTo(0xFF), $"Pixel {i} R");
      }
      Assert.That(result[dst + 3], Is.EqualTo(255), $"Pixel {i} A");
    }
  }

  [Test]
  public void Indexed1ToBgra_WithAlphaTable_UsesAlphaFromTable() {
    byte[] palette = [0xFF, 0x00, 0x00, 0x00, 0xFF, 0x00];
    byte[] alphaTable = [0x20, 0xC0];
    byte[] data = [0b10000000];

    var result = PixelConverter.Indexed1ToBgra(data, palette, 8);

    Assert.That(result[3], Is.EqualTo(255));

    var resultWithAlpha = PixelConverter.Indexed1ToBgra(data, palette, 8, alphaTable);

    Assert.That(resultWithAlpha[3], Is.EqualTo(0xC0));
    Assert.That(resultWithAlpha[7], Is.EqualTo(0x20));
  }

  [Test]
  public void Rgba32ToRgb24_DropsAlpha() {
    byte[] rgba = [0xAA, 0xBB, 0xCC, 0x80];

    var result = PixelConverter.Rgba32ToRgb24(rgba, 1);

    Assert.That(result, Has.Length.EqualTo(3));
    Assert.That(result[0], Is.EqualTo(0xAA));
    Assert.That(result[1], Is.EqualTo(0xBB));
    Assert.That(result[2], Is.EqualTo(0xCC));
  }

  [Test]
  public void Rgba32ToRgb24_EmptyInput() {
    var result = PixelConverter.Rgba32ToRgb24([], 0);

    Assert.That(result, Is.Empty);
  }

  [Test]
  public void Rgba32ToRgb24_LargeData_SimdPath() {
    const int count = 64;
    var rgba = new byte[count * 4];
    for (var i = 0; i < count; ++i) {
      rgba[i * 4] = (byte)(i & 0xFF);
      rgba[i * 4 + 1] = (byte)((i * 2) & 0xFF);
      rgba[i * 4 + 2] = (byte)((i * 3) & 0xFF);
      rgba[i * 4 + 3] = 0xFF;
    }

    var result = PixelConverter.Rgba32ToRgb24(rgba, count);

    Assert.That(result, Has.Length.EqualTo(count * 3));
    for (var i = 0; i < count; ++i) {
      Assert.That(result[i * 3], Is.EqualTo((byte)(i & 0xFF)), $"Pixel {i} R");
      Assert.That(result[i * 3 + 1], Is.EqualTo((byte)((i * 2) & 0xFF)), $"Pixel {i} G");
      Assert.That(result[i * 3 + 2], Is.EqualTo((byte)((i * 3) & 0xFF)), $"Pixel {i} B");
    }
  }

  [Test]
  public void Rgb24ToRgba32_AddsAlpha255() {
    byte[] rgb = [0xAA, 0xBB, 0xCC];

    var result = PixelConverter.Rgb24ToRgba32(rgb, 1);

    Assert.That(result, Has.Length.EqualTo(4));
    Assert.That(result[0], Is.EqualTo(0xAA));
    Assert.That(result[1], Is.EqualTo(0xBB));
    Assert.That(result[2], Is.EqualTo(0xCC));
    Assert.That(result[3], Is.EqualTo(255));
  }

  [Test]
  public void Rgb24ToRgba32_LargeData_SimdPath() {
    const int count = 64;
    var rgb = new byte[count * 3];
    for (var i = 0; i < count; ++i) {
      rgb[i * 3] = (byte)(i & 0xFF);
      rgb[i * 3 + 1] = (byte)((i * 2) & 0xFF);
      rgb[i * 3 + 2] = (byte)((i * 3) & 0xFF);
    }

    var result = PixelConverter.Rgb24ToRgba32(rgb, count);

    Assert.That(result, Has.Length.EqualTo(count * 4));
    for (var i = 0; i < count; ++i) {
      Assert.That(result[i * 4], Is.EqualTo((byte)(i & 0xFF)), $"Pixel {i} R");
      Assert.That(result[i * 4 + 1], Is.EqualTo((byte)((i * 2) & 0xFF)), $"Pixel {i} G");
      Assert.That(result[i * 4 + 2], Is.EqualTo((byte)((i * 3) & 0xFF)), $"Pixel {i} B");
      Assert.That(result[i * 4 + 3], Is.EqualTo(255), $"Pixel {i} A");
    }
  }

  [Test]
  public void Gray8ToRgb24_ReplicatesChannel() {
    byte[] gray = [128];

    var result = PixelConverter.Gray8ToRgb24(gray, 1);

    Assert.That(result, Has.Length.EqualTo(3));
    Assert.That(result[0], Is.EqualTo(128));
    Assert.That(result[1], Is.EqualTo(128));
    Assert.That(result[2], Is.EqualTo(128));
  }

  [Test]
  public void Gray8ToRgb24_LargeData_SimdPath() {
    const int count = 64;
    var gray = new byte[count];
    for (var i = 0; i < count; ++i)
      gray[i] = (byte)(i * 4);

    var result = PixelConverter.Gray8ToRgb24(gray, count);

    Assert.That(result, Has.Length.EqualTo(count * 3));
    for (var i = 0; i < count; ++i) {
      var expected = (byte)(i * 4);
      Assert.That(result[i * 3], Is.EqualTo(expected), $"Pixel {i} R");
      Assert.That(result[i * 3 + 1], Is.EqualTo(expected), $"Pixel {i} G");
      Assert.That(result[i * 3 + 2], Is.EqualTo(expected), $"Pixel {i} B");
    }
  }

  [Test]
  public void BandSequentialToInterleaved_3Band() {
    // BSQ: R0,R1,R2, G0,G1,G2, B0,B1,B2
    byte[] bsq = [10, 20, 30, 40, 50, 60, 70, 80, 90];

    var result = PixelConverter.BandSequentialToInterleaved(bsq, 3, 3);

    Assert.That(result, Has.Length.EqualTo(9));
    Assert.That(result[0], Is.EqualTo(10)); // R0
    Assert.That(result[1], Is.EqualTo(40)); // G0
    Assert.That(result[2], Is.EqualTo(70)); // B0
    Assert.That(result[3], Is.EqualTo(20)); // R1
    Assert.That(result[4], Is.EqualTo(50)); // G1
    Assert.That(result[5], Is.EqualTo(80)); // B1
    Assert.That(result[6], Is.EqualTo(30)); // R2
    Assert.That(result[7], Is.EqualTo(60)); // G2
    Assert.That(result[8], Is.EqualTo(90)); // B2
  }

  [Test]
  public void InterleavedToBandSequential_3Band() {
    // Interleaved: R0,G0,B0, R1,G1,B1, R2,G2,B2
    byte[] interleaved = [10, 40, 70, 20, 50, 80, 30, 60, 90];

    var result = PixelConverter.InterleavedToBandSequential(interleaved, 3, 3);

    Assert.That(result, Has.Length.EqualTo(9));
    Assert.That(result[0], Is.EqualTo(10)); // R0
    Assert.That(result[1], Is.EqualTo(20)); // R1
    Assert.That(result[2], Is.EqualTo(30)); // R2
    Assert.That(result[3], Is.EqualTo(40)); // G0
    Assert.That(result[4], Is.EqualTo(50)); // G1
    Assert.That(result[5], Is.EqualTo(60)); // G2
    Assert.That(result[6], Is.EqualTo(70)); // B0
    Assert.That(result[7], Is.EqualTo(80)); // B1
    Assert.That(result[8], Is.EqualTo(90)); // B2
  }

  [Test]
  public void BandSequentialToInterleaved_RoundTrip() {
    const int count = 48;
    var bsq = new byte[count * 3];
    for (var i = 0; i < bsq.Length; ++i)
      bsq[i] = (byte)(i & 0xFF);

    var interleaved = PixelConverter.BandSequentialToInterleaved(bsq, count, 3);
    var back = PixelConverter.InterleavedToBandSequential(interleaved, count, 3);

    Assert.That(back, Is.EqualTo(bsq));
  }

  [Test]
  public void BandSequentialToInterleaved_LargeData_SimdPath() {
    const int count = 64;
    var bsq = new byte[count * 3];
    for (var i = 0; i < bsq.Length; ++i)
      bsq[i] = (byte)(i & 0xFF);

    var result = PixelConverter.BandSequentialToInterleaved(bsq, count, 3);

    Assert.That(result, Has.Length.EqualTo(count * 3));
    for (var i = 0; i < count; ++i) {
      Assert.That(result[i * 3], Is.EqualTo(bsq[i]), $"Pixel {i} band 0");
      Assert.That(result[i * 3 + 1], Is.EqualTo(bsq[count + i]), $"Pixel {i} band 1");
      Assert.That(result[i * 3 + 2], Is.EqualTo(bsq[count * 2 + i]), $"Pixel {i} band 2");
    }
  }

  [Test]
  public void InterleavedToBandSequential_LargeData_SimdPath() {
    const int count = 64;
    var interleaved = new byte[count * 3];
    for (var i = 0; i < interleaved.Length; ++i)
      interleaved[i] = (byte)(i & 0xFF);

    var result = PixelConverter.InterleavedToBandSequential(interleaved, count, 3);

    Assert.That(result, Has.Length.EqualTo(count * 3));
    for (var i = 0; i < count; ++i) {
      Assert.That(result[i], Is.EqualTo(interleaved[i * 3]), $"Pixel {i} band 0");
      Assert.That(result[count + i], Is.EqualTo(interleaved[i * 3 + 1]), $"Pixel {i} band 1");
      Assert.That(result[count * 2 + i], Is.EqualTo(interleaved[i * 3 + 2]), $"Pixel {i} band 2");
    }
  }

  [Test]
  public void Convert_Rgba32ToRgb24_DirectRoute() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgba32,
      PixelData = [0xAA, 0xBB, 0xCC, 0x80],
    };

    var result = PixelConverter.Convert(image, PixelFormat.Rgb24);

    Assert.That(result.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAA));
    Assert.That(result.PixelData[1], Is.EqualTo(0xBB));
    Assert.That(result.PixelData[2], Is.EqualTo(0xCC));
  }

  [Test]
  public void Convert_Rgb24ToRgba32_DirectRoute() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [0xAA, 0xBB, 0xCC],
    };

    var result = PixelConverter.Convert(image, PixelFormat.Rgba32);

    Assert.That(result.Format, Is.EqualTo(PixelFormat.Rgba32));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAA));
    Assert.That(result.PixelData[1], Is.EqualTo(0xBB));
    Assert.That(result.PixelData[2], Is.EqualTo(0xCC));
    Assert.That(result.PixelData[3], Is.EqualTo(255));
  }

  [Test]
  public void Convert_Gray8ToRgb24_DirectRoute() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Gray8,
      PixelData = [200],
    };

    var result = PixelConverter.Convert(image, PixelFormat.Rgb24);

    Assert.That(result.Format, Is.EqualTo(PixelFormat.Rgb24));
    Assert.That(result.PixelData[0], Is.EqualTo(200));
    Assert.That(result.PixelData[1], Is.EqualTo(200));
    Assert.That(result.PixelData[2], Is.EqualTo(200));
  }

  [Test]
  public void BgraToRgb_LargeData_SimdPath() {
    const int count = 64;
    var bgra = new byte[count * 4];
    for (var i = 0; i < count; ++i) {
      bgra[i * 4] = (byte)(i & 0xFF);
      bgra[i * 4 + 1] = (byte)((i * 2) & 0xFF);
      bgra[i * 4 + 2] = (byte)((i * 3) & 0xFF);
      bgra[i * 4 + 3] = 0xFF;
    }

    var result = PixelConverter.BgraToRgb(bgra, count);

    Assert.That(result, Has.Length.EqualTo(count * 3));
    for (var i = 0; i < count; ++i) {
      Assert.That(result[i * 3], Is.EqualTo((byte)((i * 3) & 0xFF)), $"Pixel {i} R");
      Assert.That(result[i * 3 + 1], Is.EqualTo((byte)((i * 2) & 0xFF)), $"Pixel {i} G");
      Assert.That(result[i * 3 + 2], Is.EqualTo((byte)(i & 0xFF)), $"Pixel {i} B");
    }
  }

  [Test]
  public void BgraToBgr_LargeData_SimdPath() {
    const int count = 64;
    var bgra = new byte[count * 4];
    for (var i = 0; i < count; ++i) {
      bgra[i * 4] = (byte)(i & 0xFF);
      bgra[i * 4 + 1] = (byte)((i * 2) & 0xFF);
      bgra[i * 4 + 2] = (byte)((i * 3) & 0xFF);
      bgra[i * 4 + 3] = 0xFF;
    }

    var result = PixelConverter.BgraToBgr(bgra, count);

    Assert.That(result, Has.Length.EqualTo(count * 3));
    for (var i = 0; i < count; ++i) {
      Assert.That(result[i * 3], Is.EqualTo((byte)(i & 0xFF)), $"Pixel {i} B");
      Assert.That(result[i * 3 + 1], Is.EqualTo((byte)((i * 2) & 0xFF)), $"Pixel {i} G");
      Assert.That(result[i * 3 + 2], Is.EqualTo((byte)((i * 3) & 0xFF)), $"Pixel {i} R");
    }
  }

  [Test]
  public void RoundTrip_Rgba32ThroughRgb24AndBack_PreservesRgb() {
    byte[] original = [0xAA, 0xBB, 0xCC, 0xFF];

    var rgb = PixelConverter.Rgba32ToRgb24(original, 1);
    var back = PixelConverter.Rgb24ToRgba32(rgb, 1);

    Assert.That(back[0], Is.EqualTo(original[0]));
    Assert.That(back[1], Is.EqualTo(original[1]));
    Assert.That(back[2], Is.EqualTo(original[2]));
    Assert.That(back[3], Is.EqualTo(255));
  }

  [Test]
  public void BandSequentialToInterleaved_GenericBands() {
    // 4-band: R0,R1, G0,G1, B0,B1, A0,A1
    byte[] bsq = [10, 20, 30, 40, 50, 60, 70, 80];

    var result = PixelConverter.BandSequentialToInterleaved(bsq, 2, 4);

    Assert.That(result, Has.Length.EqualTo(8));
    Assert.That(result[0], Is.EqualTo(10)); // R0
    Assert.That(result[1], Is.EqualTo(30)); // G0
    Assert.That(result[2], Is.EqualTo(50)); // B0
    Assert.That(result[3], Is.EqualTo(70)); // A0
    Assert.That(result[4], Is.EqualTo(20)); // R1
    Assert.That(result[5], Is.EqualTo(40)); // G1
    Assert.That(result[6], Is.EqualTo(60)); // B1
    Assert.That(result[7], Is.EqualTo(80)); // A1
  }

  // ── Gray16 hub route tests ────────────────────────────────────────────────

  [Test]
  public void Gray16ToBgra_SinglePixel_TakesHighByteExpandsToRgb() {
    byte[] gray16 = [0x80, 0x40]; // BE: 0x8040

    var result = PixelConverter.Gray16ToBgra(gray16, 1);

    Assert.That(result, Has.Length.EqualTo(4));
    Assert.That(result[0], Is.EqualTo(0x80));
    Assert.That(result[1], Is.EqualTo(0x80));
    Assert.That(result[2], Is.EqualTo(0x80));
    Assert.That(result[3], Is.EqualTo(255));
  }

  [Test]
  public void BgraToGray16_WhitePixel_ProducesFullScale() {
    byte[] bgra = [255, 255, 255, 255];

    var result = PixelConverter.BgraToGray16(bgra, 1);

    Assert.That(result, Has.Length.EqualTo(2));
    Assert.That(result[0], Is.EqualTo(255)); // hi = 255
    Assert.That(result[1], Is.EqualTo(255)); // lo = 255 (255*257 = 0xFFFF)
  }

  [Test]
  public void BgraToGray16_BlackPixel_ProducesZero() {
    byte[] bgra = [0, 0, 0, 255];

    var result = PixelConverter.BgraToGray16(bgra, 1);

    Assert.That(result[0], Is.EqualTo(0));
    Assert.That(result[1], Is.EqualTo(0));
  }

  // ── 16-bit upscale from Bgra32 tests ─────────────────────────────────────

  [Test]
  public void BgraToRgb48_SinglePixel_UpscalesVia257() {
    byte[] bgra = [0xCC, 0xBB, 0xAA, 0xFF]; // B=CC, G=BB, R=AA

    var result = PixelConverter.BgraToRgb48(bgra, 1);

    Assert.That(result, Has.Length.EqualTo(6));
    Assert.That(result[0], Is.EqualTo(0xAA)); // R hi
    Assert.That(result[1], Is.EqualTo(0xAA)); // R lo (AA*257 = 0xAAAA)
    Assert.That(result[2], Is.EqualTo(0xBB)); // G hi
    Assert.That(result[3], Is.EqualTo(0xBB)); // G lo
    Assert.That(result[4], Is.EqualTo(0xCC)); // B hi
    Assert.That(result[5], Is.EqualTo(0xCC)); // B lo
  }

  [Test]
  public void BgraToRgba64_SinglePixel_UpscalesAllChannels() {
    byte[] bgra = [0xCC, 0xBB, 0xAA, 0x80]; // B=CC, G=BB, R=AA, A=80

    var result = PixelConverter.BgraToRgba64(bgra, 1);

    Assert.That(result, Has.Length.EqualTo(8));
    Assert.That(result[0], Is.EqualTo(0xAA)); // R hi
    Assert.That(result[1], Is.EqualTo(0xAA)); // R lo
    Assert.That(result[6], Is.EqualTo(0x80)); // A hi
    Assert.That(result[7], Is.EqualTo(0x80)); // A lo
  }

  // ── Direct 16↔16 route tests ─────────────────────────────────────────────

  [Test]
  public void Rgb48ToRgba64_AddsOpaqueAlpha() {
    byte[] rgb48 = [0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF];

    var result = PixelConverter.Rgb48ToRgba64(rgb48, 1);

    Assert.That(result, Has.Length.EqualTo(8));
    Assert.That(result[0], Is.EqualTo(0xAA));
    Assert.That(result[1], Is.EqualTo(0xBB));
    Assert.That(result[4], Is.EqualTo(0xEE));
    Assert.That(result[5], Is.EqualTo(0xFF));
    Assert.That(result[6], Is.EqualTo(0xFF)); // A hi
    Assert.That(result[7], Is.EqualTo(0xFF)); // A lo
  }

  [Test]
  public void Rgba64ToRgb48_DropsAlpha() {
    byte[] rgba64 = [0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF, 0x80, 0x40];

    var result = PixelConverter.Rgba64ToRgb48(rgba64, 1);

    Assert.That(result, Has.Length.EqualTo(6));
    Assert.That(result[0], Is.EqualTo(0xAA));
    Assert.That(result[1], Is.EqualTo(0xBB));
    Assert.That(result[4], Is.EqualTo(0xEE));
    Assert.That(result[5], Is.EqualTo(0xFF));
  }

  [Test]
  public void RoundTrip_Rgb48ToRgba64AndBack_PreservesChannels() {
    byte[] original = [0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC];

    var rgba64 = PixelConverter.Rgb48ToRgba64(original, 1);
    var back = PixelConverter.Rgba64ToRgb48(rgba64, 1);

    Assert.That(back, Is.EqualTo(original));
  }

  [Test]
  public void Gray16ToRgb48_ExpandsToAllChannels() {
    byte[] gray16 = [0xAB, 0xCD];

    var result = PixelConverter.Gray16ToRgb48(gray16, 1);

    Assert.That(result, Has.Length.EqualTo(6));
    Assert.That(result[0], Is.EqualTo(0xAB));
    Assert.That(result[1], Is.EqualTo(0xCD));
    Assert.That(result[2], Is.EqualTo(0xAB));
    Assert.That(result[3], Is.EqualTo(0xCD));
    Assert.That(result[4], Is.EqualTo(0xAB));
    Assert.That(result[5], Is.EqualTo(0xCD));
  }

  [Test]
  public void Gray16ToRgba64_ExpandsWithOpaqueAlpha() {
    byte[] gray16 = [0xAB, 0xCD];

    var result = PixelConverter.Gray16ToRgba64(gray16, 1);

    Assert.That(result, Has.Length.EqualTo(8));
    Assert.That(result[0], Is.EqualTo(0xAB));
    Assert.That(result[1], Is.EqualTo(0xCD));
    Assert.That(result[6], Is.EqualTo(0xFF));
    Assert.That(result[7], Is.EqualTo(0xFF));
  }

  [Test]
  public void Rgb48ToGray16_WhitePixel_ProducesMaxValue() {
    byte[] rgb48 = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];

    var result = PixelConverter.Rgb48ToGray16(rgb48, 1);

    // Luminance of white = (65535*19595 + 65535*38470 + 65535*7471) >> 16
    var expected = (65535L * 19595 + 65535L * 38470 + 65535L * 7471) >> 16;
    var actual = (result[0] << 8) | result[1];
    Assert.That(actual, Is.EqualTo((int)expected));
  }

  [Test]
  public void Rgb48ToGray16_BlackPixel_ProducesZero() {
    byte[] rgb48 = [0, 0, 0, 0, 0, 0];

    var result = PixelConverter.Rgb48ToGray16(rgb48, 1);

    Assert.That(result[0], Is.EqualTo(0));
    Assert.That(result[1], Is.EqualTo(0));
  }

  [Test]
  public void Rgba64ToGray16_IgnoresAlpha() {
    byte[] rgba64 = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x00];

    var result = PixelConverter.Rgba64ToGray16(rgba64, 1);
    var result2 = PixelConverter.Rgb48ToGray16([0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF], 1);

    Assert.That(result, Is.EqualTo(result2));
  }

  // ── Direct 16→8 shortcut tests ────────────────────────────────────────────

  [Test]
  public void Rgb48ToRgb24_TakesHighBytes() {
    byte[] rgb48 = [0xAA, 0x11, 0xBB, 0x22, 0xCC, 0x33];

    var result = PixelConverter.Rgb48ToRgb24(rgb48, 1);

    Assert.That(result, Has.Length.EqualTo(3));
    Assert.That(result[0], Is.EqualTo(0xAA));
    Assert.That(result[1], Is.EqualTo(0xBB));
    Assert.That(result[2], Is.EqualTo(0xCC));
  }

  [Test]
  public void Rgba64ToRgba32_TakesHighBytes() {
    byte[] rgba64 = [0xAA, 0x11, 0xBB, 0x22, 0xCC, 0x33, 0xDD, 0x44];

    var result = PixelConverter.Rgba64ToRgba32(rgba64, 1);

    Assert.That(result, Has.Length.EqualTo(4));
    Assert.That(result[0], Is.EqualTo(0xAA));
    Assert.That(result[1], Is.EqualTo(0xBB));
    Assert.That(result[2], Is.EqualTo(0xCC));
    Assert.That(result[3], Is.EqualTo(0xDD));
  }

  [Test]
  public void Gray16ToGray8_TakesHighByte() {
    byte[] gray16 = [0xAB, 0xCD];

    var result = PixelConverter.Gray16ToGray8(gray16, 1);

    Assert.That(result, Has.Length.EqualTo(1));
    Assert.That(result[0], Is.EqualTo(0xAB));
  }

  // ── Direct 8→16 upscale tests ─────────────────────────────────────────────

  [Test]
  public void Rgb24ToRgb48_UpscalesVia257() {
    byte[] rgb24 = [0xAA, 0xBB, 0xCC];

    var result = PixelConverter.Rgb24ToRgb48(rgb24, 1);

    Assert.That(result, Has.Length.EqualTo(6));
    Assert.That(result[0], Is.EqualTo(0xAA));
    Assert.That(result[1], Is.EqualTo(0xAA));
    Assert.That(result[2], Is.EqualTo(0xBB));
    Assert.That(result[3], Is.EqualTo(0xBB));
    Assert.That(result[4], Is.EqualTo(0xCC));
    Assert.That(result[5], Is.EqualTo(0xCC));
  }

  [Test]
  public void Rgba32ToRgba64_UpscalesAllChannels() {
    byte[] rgba32 = [0xAA, 0xBB, 0xCC, 0x80];

    var result = PixelConverter.Rgba32ToRgba64(rgba32, 1);

    Assert.That(result, Has.Length.EqualTo(8));
    Assert.That(result[0], Is.EqualTo(0xAA));
    Assert.That(result[1], Is.EqualTo(0xAA));
    Assert.That(result[6], Is.EqualTo(0x80));
    Assert.That(result[7], Is.EqualTo(0x80));
  }

  [Test]
  public void Gray8ToGray16_UpscalesVia257() {
    byte[] gray8 = [0xAB];

    var result = PixelConverter.Gray8ToGray16(gray8, 1);

    Assert.That(result, Has.Length.EqualTo(2));
    Assert.That(result[0], Is.EqualTo(0xAB));
    Assert.That(result[1], Is.EqualTo(0xAB));
  }

  [Test]
  public void Gray8ToGray16_Zero_ProducesZero() {
    var result = PixelConverter.Gray8ToGray16([0], 1);

    Assert.That(result[0], Is.EqualTo(0));
    Assert.That(result[1], Is.EqualTo(0));
  }

  [Test]
  public void Gray8ToGray16_Max_ProducesMax() {
    var result = PixelConverter.Gray8ToGray16([255], 1);

    Assert.That(result[0], Is.EqualTo(255));
    Assert.That(result[1], Is.EqualTo(255));
  }

  // ── Round-trip precision tests ────────────────────────────────────────────

  [Test]
  public void RoundTrip_Rgb24ToRgb48AndBack_PreservesValues() {
    byte[] original = [0x12, 0x34, 0x56];

    var rgb48 = PixelConverter.Rgb24ToRgb48(original, 1);
    var back = PixelConverter.Rgb48ToRgb24(rgb48, 1);

    Assert.That(back, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_Rgba32ToRgba64AndBack_PreservesValues() {
    byte[] original = [0x12, 0x34, 0x56, 0x78];

    var rgba64 = PixelConverter.Rgba32ToRgba64(original, 1);
    var back = PixelConverter.Rgba64ToRgba32(rgba64, 1);

    Assert.That(back, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_Gray8ToGray16AndBack_PreservesValues() {
    byte[] original = [0xAB];

    var gray16 = PixelConverter.Gray8ToGray16(original, 1);
    var back = PixelConverter.Gray16ToGray8(gray16, 1);

    Assert.That(back, Is.EqualTo(original));
  }

  [Test]
  public void RoundTrip_Gray16ToRgb48ToGray16_PreservesGrayscale() {
    byte[] original = [0x80, 0x40]; // gray value

    var rgb48 = PixelConverter.Gray16ToRgb48(original, 1);
    var back = PixelConverter.Rgb48ToGray16(rgb48, 1);

    // Gray→RGB→Gray should be nearly lossless for achromatic values
    var origVal = (original[0] << 8) | original[1];
    var backVal = (back[0] << 8) | back[1];
    Assert.That(Math.Abs(origVal - backVal), Is.LessThanOrEqualTo(1));
  }

  // ── Convert method integration tests for 16-bit routes ────────────────────

  [Test]
  public void Convert_Gray16ToBgra32_ViaHub() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Gray16,
      PixelData = [0x80, 0x40],
    };

    var result = PixelConverter.Convert(image, PixelFormat.Bgra32);

    Assert.That(result.Format, Is.EqualTo(PixelFormat.Bgra32));
    Assert.That(result.PixelData[0], Is.EqualTo(0x80));
    Assert.That(result.PixelData[3], Is.EqualTo(255));
  }

  [Test]
  public void Convert_Rgb48ToRgba64_DirectRoute() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb48,
      PixelData = [0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF],
    };

    var result = PixelConverter.Convert(image, PixelFormat.Rgba64);

    Assert.That(result.Format, Is.EqualTo(PixelFormat.Rgba64));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAA));
    Assert.That(result.PixelData[6], Is.EqualTo(0xFF)); // alpha
  }

  [Test]
  public void Convert_Rgb24ToRgb48_DirectUpscale() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb24,
      PixelData = [0xAA, 0xBB, 0xCC],
    };

    var result = PixelConverter.Convert(image, PixelFormat.Rgb48);

    Assert.That(result.Format, Is.EqualTo(PixelFormat.Rgb48));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAA));
    Assert.That(result.PixelData[1], Is.EqualTo(0xAA));
  }

  [Test]
  public void Convert_Gray16ToGray8_DirectDownscale() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Gray16,
      PixelData = [0xAB, 0xCD],
    };

    var result = PixelConverter.Convert(image, PixelFormat.Gray8);

    Assert.That(result.Format, Is.EqualTo(PixelFormat.Gray8));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAB));
  }

  [Test]
  public void Convert_Gray8ToGray16_DirectUpscale() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Gray8,
      PixelData = [0x80],
    };

    var result = PixelConverter.Convert(image, PixelFormat.Gray16);

    Assert.That(result.Format, Is.EqualTo(PixelFormat.Gray16));
    Assert.That(result.PixelData[0], Is.EqualTo(0x80));
    Assert.That(result.PixelData[1], Is.EqualTo(0x80));
  }

  [Test]
  public void Convert_Rgb48ToGray16_DirectLuminance() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Rgb48,
      PixelData = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF],
    };

    var result = PixelConverter.Convert(image, PixelFormat.Gray16);

    Assert.That(result.Format, Is.EqualTo(PixelFormat.Gray16));
    var gray = (result.PixelData[0] << 8) | result.PixelData[1];
    Assert.That(gray, Is.GreaterThan(65000)); // close to 65535
  }

  [Test]
  public void Convert_Bgra32ToRgb48_UpscaleRoute() {
    var image = new RawImage {
      Width = 1,
      Height = 1,
      Format = PixelFormat.Bgra32,
      PixelData = [0xCC, 0xBB, 0xAA, 0xFF],
    };

    var result = PixelConverter.Convert(image, PixelFormat.Rgb48);

    Assert.That(result.Format, Is.EqualTo(PixelFormat.Rgb48));
    Assert.That(result.PixelData[0], Is.EqualTo(0xAA)); // R hi
    Assert.That(result.PixelData[2], Is.EqualTo(0xBB)); // G hi
    Assert.That(result.PixelData[4], Is.EqualTo(0xCC)); // B hi
  }

  [Test]
  public void MultiplePixels_Gray16ToBgra_AllProcessed() {
    byte[] gray16 = [0x40, 0x00, 0x80, 0x00, 0xC0, 0x00];

    var result = PixelConverter.Gray16ToBgra(gray16, 3);

    Assert.That(result, Has.Length.EqualTo(12));
    Assert.That(result[0], Is.EqualTo(0x40));
    Assert.That(result[4], Is.EqualTo(0x80));
    Assert.That(result[8], Is.EqualTo(0xC0));
  }
}
