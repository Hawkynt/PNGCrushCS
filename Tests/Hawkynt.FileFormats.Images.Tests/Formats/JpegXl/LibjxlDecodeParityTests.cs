using System;
using System.IO;
using NUnit.Framework;

namespace FileFormat.JpegXl.Tests;

/// <summary>
/// What this reader returns for files libjxl wrote, checked against what libjxl
/// returns for the same files.
/// </summary>
/// <remarks>
/// Both fixtures are the same 64x48 picture encoded losslessly by <c>cjxl</c>,
/// once at effort 1 and once at effort 9, each with <c>djxl</c>'s own decode
/// beside it. The pair is here because they used to come out on opposite sides
/// of the only line that matters: the effort-1 file decoded sample for sample,
/// and the effort-9 file decoded to a picture that differed from libjxl's in
/// 1,383 of its 3,072 pixels and was handed back anyway. A caller had no way to
/// tell the two apart.
///
/// Both decode exactly now. What is under test stays the guarantee rather than
/// the count that happens to satisfy it today: a file decodes to what libjxl
/// decodes it to, or it is refused.
/// </remarks>
[TestFixture]
public sealed class LibjxlDecodeParityTests {

  private static byte[] _Fixture(string name) {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
    Assert.That(File.Exists(path), Is.True, $"Test fixture missing: {path}");
    return File.ReadAllBytes(path);
  }

  /// <param name="name">
  /// <c>cjxl_flat_tuned_predictor</c> states weighted-predictor parameters of its
  /// own instead of taking the defaults, which is the case that used to be read
  /// past and predicted with the defaults anyway; <c>cjxl_palette_effort7</c>
  /// carries a palette transform, which used to be refused outright.
  /// </param>
  [TestCase("cjxl_lossless_effort1")]
  [TestCase("cjxl_lossless_effort9")]
  [TestCase("cjxl_flat_tuned_predictor")]
  [TestCase("cjxl_palette_effort7")]
  public void ALosslessFileDecodesToWhatLibjxlDecodesItTo(string name) {
    var file = JpegXlReader.FromBytes(_Fixture(name + ".jxl"));
    var (width, height, expected) = _ReadPpm(_Fixture(name + ".ppm"));

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(width));
      Assert.That(file.Height, Is.EqualTo(height));
      Assert.That(file.ComponentCount, Is.EqualTo(3));
    });

    Assert.That(file.PixelData.Length, Is.EqualTo(expected.Length));
    for (var i = 0; i < expected.Length; ++i)
      if (file.PixelData[i] != expected[i])
        Assert.Fail($"{name}: sample {i} is {file.PixelData[i]}, libjxl decodes it to {expected[i]}.");
  }

  private static (int Width, int Height, byte[] Pixels) _ReadPpm(byte[] ppm) {
    var at = 0;
    string Token() {
      while (at < ppm.Length && char.IsWhiteSpace((char)ppm[at]))
        ++at;
      var start = at;
      while (at < ppm.Length && !char.IsWhiteSpace((char)ppm[at]))
        ++at;
      return System.Text.Encoding.ASCII.GetString(ppm, start, at - start);
    }

    Assert.That(Token(), Is.EqualTo("P6"));
    var width = int.Parse(Token());
    var height = int.Parse(Token());
    Assert.That(Token(), Is.EqualTo("255"));
    ++at; // the single whitespace byte that ends the header

    var pixels = new byte[width * height * 3];
    Array.Copy(ppm, at, pixels, 0, pixels.Length);
    return (width, height, pixels);
  }
}
