using System;
using System.IO;
using FileFormat.Core;
using FileFormat.MadStudio;

namespace Conformance.Recoil.Tests;

/// <summary>
/// Mad Studio's five character modes come off one encoder with the mode as a parameter, which the
/// format enum cannot carry — so these drive it directly rather than through the registry.
/// </summary>
[TestFixture]
public sealed class MadStudioConformanceTests {

  private static readonly (MadStudioMode Mode, string Extension)[] _Modes = [
    (MadStudioMode.Antic2, ".an2"),
    (MadStudioMode.Antic4, ".an4"),
    (MadStudioMode.Antic5, ".an5"),
    (MadStudioMode.Graphics1, ".gr1"),
    (MadStudioMode.Graphics2, ".gr2"),
  ];

  private static RawImage _Sample() {
    const int width = MadStudioLayout.DisplayWidth;
    const int height = MadStudioLayout.DisplayHeight;
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
  public void EveryMode_IsReadableByRecoil((MadStudioMode Mode, string Extension) mode) {
    RecoilOracle.RequireAvailable();

    var encoded = MadStudioWriter.ToBytes(MadStudioFile.FromRawImage(_Sample(), mode.Mode));
    var path = Path.Combine(Path.GetTempPath(), $"madstudioconf_{mode.Mode}{mode.Extension}");
    try {
      File.WriteAllBytes(path, encoded);
      var (decoded, output) = RecoilOracle.TryDecode(path);
      Assert.That(decoded, Is.True, $"{mode.Mode}: RECOIL rejected our {encoded.Length}-byte {mode.Extension} — {output}");
    } finally {
      try { File.Delete(path); } catch { /* best effort */ }
    }
  }
}
