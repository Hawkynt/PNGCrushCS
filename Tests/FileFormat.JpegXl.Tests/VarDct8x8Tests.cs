using System;
using System.IO;
using FileFormat.JpegXl;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// Bit-position diagnostic for 8x8_vardct.jxl. djxl 0.11.2 decodes this
/// fixture to a near-uniform image of RGB(128, 0, 2). Smallest VarDCT
/// fixture available — exercises the full DC/AC/IDCT/Gaborish/EPF pipeline
/// at the minimum complexity (single 8x8 block).
/// </summary>
[TestFixture]
public sealed class VarDct8x8Tests {

  [Test]
  public void Vardct_8x8_DiagnosticDump() {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "8x8_vardct.jxl");
    var bytes = File.ReadAllBytes(path);

    var ok = JpegXlReader.TryReadSpecImage(bytes, out var meta, out var img);
    TestContext.Out.WriteLine(
      $"ok={ok}, meta=[{meta.Width}x{meta.Height} {meta.BitsPerSample}u xyb={meta.IsXybEncoded} modular={meta.IsModularFrame}], " +
      $"image={(img != null ? img.GetType().Name : "null")}");
    Assert.Pass();
  }
}
