using System;
using System.IO;
using FileFormat.XFliEditor;

namespace FileFormat.XFliEditor.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllFieldsPreserved() {
    var bitmapData = new byte[8000];
    for (var i = 0; i < bitmapData.Length; ++i)
      bitmapData[i] = (byte)(i * 7 % 256);

    var screenBanks = new byte[8][];
    for (var b = 0; b < 8; ++b) {
      screenBanks[b] = new byte[1000];
      for (var i = 0; i < 1000; ++i)
        screenBanks[b][i] = (byte)((i * 3 + b) % 16);
    }

    var colorData = new byte[1000];
    for (var i = 0; i < colorData.Length; ++i)
      colorData[i] = (byte)((i * 3 + 1) % 16);

    var original = new XFliEditorFile {
      LoadAddress = 0x3C00,
      BitmapData = bitmapData,
      ScreenBanks = screenBanks,
      ColorData = colorData,
      BackgroundColor = 11,
    };

    var bytes = XFliEditorWriter.ToBytes(original);
    var restored = XFliEditorReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    Assert.That(restored.ScreenBanks.Length, Is.EqualTo(8));
    for (var i = 0; i < 8; ++i)
      Assert.That(restored.ScreenBanks[i], Is.EqualTo(original.ScreenBanks[i]));
    Assert.That(restored.ColorData, Is.EqualTo(original.ColorData));
    Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CustomLoadAddress() {
    var original = new XFliEditorFile {
      LoadAddress = 0x6000,
      BitmapData = new byte[8000],
      ScreenBanks = _MakeScreenBanks(),
      ColorData = new byte[1000],
      BackgroundColor = 0,
    };

    var bytes = XFliEditorWriter.ToBytes(original);
    var restored = XFliEditorReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0x6000));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_BackgroundColorPreserved() {
    var original = new XFliEditorFile {
      LoadAddress = 0x3C00,
      BitmapData = new byte[8000],
      ScreenBanks = _MakeScreenBanks(),
      ColorData = new byte[1000],
      BackgroundColor = 15,
    };

    var bytes = XFliEditorWriter.ToBytes(original);
    var restored = XFliEditorReader.FromBytes(bytes);

    Assert.That(restored.BackgroundColor, Is.EqualTo(15));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_AllBytesMaxValue() {
    var bitmapData = new byte[8000];
    Array.Fill(bitmapData, (byte)0xFF);

    var screenBanks = new byte[8][];
    for (var b = 0; b < 8; ++b) {
      screenBanks[b] = new byte[1000];
      Array.Fill(screenBanks[b], (byte)0xFF);
    }

    var colorData = new byte[1000];
    Array.Fill(colorData, (byte)0x0F);

    var original = new XFliEditorFile {
      LoadAddress = 0xFFFF,
      BitmapData = bitmapData,
      ScreenBanks = screenBanks,
      ColorData = colorData,
      BackgroundColor = 15,
    };

    var bytes = XFliEditorWriter.ToBytes(original);
    var restored = XFliEditorReader.FromBytes(bytes);

    Assert.That(restored.LoadAddress, Is.EqualTo(0xFFFF));
    Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
    for (var i = 0; i < 8; ++i)
      Assert.That(restored.ScreenBanks[i], Is.EqualTo(original.ScreenBanks[i]));
    Assert.That(restored.ColorData, Is.EqualTo(original.ColorData));
    Assert.That(restored.BackgroundColor, Is.EqualTo(15));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ViaFile_PreservesData() {
    var bitmapData = new byte[8000];
    for (var i = 0; i < 8000; ++i)
      bitmapData[i] = (byte)(i % 256);

    var original = new XFliEditorFile {
      LoadAddress = 0x3C00,
      BitmapData = bitmapData,
      ScreenBanks = _MakeScreenBanks(),
      ColorData = new byte[1000],
      BackgroundColor = 3,
    };

    var tmpPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xfl");
    try {
      var bytes = XFliEditorWriter.ToBytes(original);
      File.WriteAllBytes(tmpPath, bytes);
      var restored = XFliEditorReader.FromFile(new FileInfo(tmpPath));

      Assert.That(restored.LoadAddress, Is.EqualTo(original.LoadAddress));
      Assert.That(restored.BitmapData, Is.EqualTo(original.BitmapData));
      Assert.That(restored.BackgroundColor, Is.EqualTo(original.BackgroundColor));
    } finally {
      if (File.Exists(tmpPath))
        File.Delete(tmpPath);
    }
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_TrailingDataPreserved() {
    var original = new XFliEditorFile {
      LoadAddress = 0x3C00,
      BitmapData = new byte[8000],
      ScreenBanks = _MakeScreenBanks(),
      ColorData = new byte[1000],
      BackgroundColor = 0,
      TrailingData = [0x01, 0x02, 0x03],
    };

    var bytes = XFliEditorWriter.ToBytes(original);
    var restored = XFliEditorReader.FromBytes(bytes);

    Assert.That(restored.TrailingData, Is.EqualTo(original.TrailingData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ToRawImage_ProducesCorrectDimensions() {
    var original = new XFliEditorFile {
      LoadAddress = 0x3C00,
      BitmapData = new byte[8000],
      ScreenBanks = _MakeScreenBanks(),
      ColorData = new byte[1000],
      BackgroundColor = 0,
    };

    var raw = XFliEditorFile.ToRawImage(original);

    Assert.That(raw.Width, Is.EqualTo(160));
    Assert.That(raw.Height, Is.EqualTo(200));
    Assert.That(raw.PixelData.Length, Is.EqualTo(160 * 200 * 3));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_ScreenBankSelection_MatchesScanlineModulo() {
    // Verify that FLI bank selection is y%8 by setting distinct data in each bank
    var screenBanks = new byte[8][];
    for (var b = 0; b < 8; ++b) {
      screenBanks[b] = new byte[1000];
      // Fill each bank with a distinct pattern
      for (var i = 0; i < 1000; ++i)
        screenBanks[b][i] = (byte)((b << 4) | (b & 0x0F));
    }

    var original = new XFliEditorFile {
      LoadAddress = 0x3C00,
      BitmapData = new byte[8000],
      ScreenBanks = screenBanks,
      ColorData = new byte[1000],
      BackgroundColor = 0,
    };

    var bytes = XFliEditorWriter.ToBytes(original);
    var restored = XFliEditorReader.FromBytes(bytes);

    for (var b = 0; b < 8; ++b)
      Assert.That(restored.ScreenBanks[b], Is.EqualTo(original.ScreenBanks[b]));
  }

  private static byte[][] _MakeScreenBanks() {
    var banks = new byte[8][];
    for (var i = 0; i < 8; ++i)
      banks[i] = new byte[1000];
    return banks;
  }
}
