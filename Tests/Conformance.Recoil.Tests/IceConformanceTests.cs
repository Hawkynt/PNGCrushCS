using System;
using System.IO;
using FileFormat.Core;
using FileFormat.InterlaceCharacterEditor;

namespace Conformance.Recoil.Tests;

/// <summary>
/// The Interlace Character Editor family shares one encoder across five formats, and which one is
/// produced is a parameter rather than something the registry can express. These drive it directly
/// so every mode is proved against the reference decoder, not just the default.
/// </summary>
[TestFixture]
public sealed class IceConformanceTests {

  private static readonly (IceMode Mode, string Extension)[] _Modes = [
    (IceMode.SuperIrg, ".irg"),
    (IceMode.SuperIrg2, ".ir2"),
    (IceMode.Cin, ".icn"),
    (IceMode.Min, ".imn"),
    (IceMode.Pcin, ".ipc"),
  ];

  private static RawImage _Sample() {
    const int width = IceLayout.DisplayWidth;
    const int height = IceLayout.DisplayHeight;
    var data = new byte[width * height * 4];
    for (var y = 0; y < height; ++y)
    for (var x = 0; x < width; ++x) {
      var o = (y * width + x) * 4;
      data[o] = (byte)(x * 255 / (width - 1));
      data[o + 1] = (byte)(y * 255 / (height - 1));
      data[o + 2] = (byte)((x / 8 + y / 8) % 2 == 0 ? 255 : 0);
      data[o + 3] = 255;
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgba32, PixelData = data };
  }

  [TestCaseSource(nameof(_Modes))]
  [Category("Conformance")]
  public void EveryMode_IsReadableByRecoil((IceMode Mode, string Extension) mode) {
    RecoilOracle.RequireAvailable();

    var encoded = IceWriter.ToBytes(IceFile.FromRawImage(_Sample(), mode.Mode));
    var path = Path.Combine(Path.GetTempPath(), $"iceconf_{mode.Mode}{mode.Extension}");
    try {
      File.WriteAllBytes(path, encoded);
      var (decoded, output) = RecoilOracle.TryDecode(path);
      Assert.That(decoded, Is.True, $"{mode.Mode}: RECOIL rejected our {encoded.Length}-byte {mode.Extension} — {output}");
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }
  }

  [TestCaseSource(nameof(_Modes))]
  [Category("Conformance")]
  public void EveryMode_RoundTripsThroughOurOwnReader((IceMode Mode, string Extension) mode) {
    var file = IceFile.FromRawImage(_Sample(), mode.Mode);
    var restored = IceReader.FromSpan(IceWriter.ToBytes(file), mode.Mode);

    Assert.Multiple(() => {
      Assert.That(restored.Header, Is.EqualTo(file.Header));
      Assert.That(restored.FontData, Is.EqualTo(file.FontData));
      Assert.That(restored.Characters1, Is.EqualTo(file.Characters1));
      Assert.That(restored.Characters2, Is.EqualTo(file.Characters2));
    });
  }
}
