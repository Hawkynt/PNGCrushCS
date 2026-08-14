using System;
using System.Diagnostics;
using Hawkynt.FileFormats.Images.Tests;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Svg.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Gradient(int width, int height) {
    var data = new byte[width * height * 3];
    for (var i = 0; i < width * height; ++i) {
      data[i * 3] = (byte)(i * 7);
      data[i * 3 + 1] = (byte)(i * 13);
      data[i * 3 + 2] = (byte)(i * 29);
    }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_Gradient_ReproducesEveryPixel() {
    var source = _Gradient(37, 11);
    var decoded = SvgFile.ToRawImage(SvgReader.FromBytes(SvgWriter.ToBytes(SvgFile.FromRawImage(source))));

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((37, 11)));
      Assert.That(PixelConverter.Convert(decoded, PixelFormat.Rgb24).PixelData, Is.EqualTo(source.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    var wide = SvgWriter.ToBytes(SvgFile.FromRawImage(_Gradient(200, 3)));
    var tall = SvgWriter.ToBytes(SvgFile.FromRawImage(_Gradient(3, 200)));

    Assert.Multiple(() => {
      Assert.That(SvgFile.ToRawImage(SvgReader.FromBytes(wide)).Width, Is.EqualTo(200));
      Assert.That(SvgFile.ToRawImage(SvgReader.FromBytes(tall)).Height, Is.EqualTo(200));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_AcceptsAFormatOtherThanItsOwn() {
    var grey = new RawImage { Width = 5, Height = 4, Format = PixelFormat.Gray8, PixelData = new byte[20] };
    var decoded = SvgFile.ToRawImage(SvgReader.FromBytes(SvgWriter.ToBytes(SvgFile.FromRawImage(grey))));

    Assert.That((decoded.Width, decoded.Height), Is.EqualTo((5, 4)));
  }

  /// <summary>
  /// The picture is embedded, not traced. What goes out is one <c>image</c> element carrying the
  /// pixels; there is no path in the file, because a bitmap turned into outlines is geometry the
  /// picture never had.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_HoldsThePictureRatherThanShapesMadeFromIt() {
    var document = Encoding.UTF8.GetString(SvgWriter.ToBytes(SvgFile.FromRawImage(_Gradient(37, 11))));

    Assert.Multiple(() => {
      Assert.That(document, Does.Contain("<image"));
      Assert.That(document, Does.Contain(SvgDataUri.PngPrefix));
      Assert.That(document, Does.Contain("viewBox=\"0 0 37 11\""));
      Assert.That(document, Does.Not.Contain("<path"));
      Assert.That(document, Does.Not.Contain("<polygon"));
      Assert.That(document, Does.Not.Contain("<rect"));
    });
  }

  /// <summary>
  /// Both spellings of the reference. <c>href</c> is what SVG 2 defines and <c>xlink:href</c> what
  /// every SVG 1.1 renderer looks for; a file with only the first is drawn blank by some of them.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_NamesTheSourceBothWaysRenderersLookForIt() {
    var document = Encoding.UTF8.GetString(SvgWriter.ToBytes(SvgFile.FromRawImage(_Gradient(9, 5))));

    Assert.Multiple(() => {
      Assert.That(document, Does.Contain(" href=\""));
      Assert.That(document, Does.Contain(":href=\""));
      Assert.That(document, Does.Contain("http://www.w3.org/1999/xlink"));
    });
  }

  /// <summary>An <c>image</c> pointing outside the document is not fetched, and draws nothing.</summary>
  [Test]
  [Category("Unit")]
  public void Render_AnImageNamingAFileIsNotFetched() {
    var document = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"4\" height=\"4\">"
                   + "<image x=\"0\" y=\"0\" width=\"4\" height=\"4\" href=\"/etc/passwd\"/></svg>";

    var image = SvgFile.ToRawImage(SvgReader.FromBytes(Encoding.UTF8.GetBytes(document)));

    Assert.Multiple(() => {
      Assert.That((image.Width, image.Height), Is.EqualTo((4, 4)));
      Assert.That(image.PixelData[3], Is.Zero, "nothing was drawn there");
    });
  }

  /// <summary>What a renderer that is not this one makes of it, at the size and to the pixel.</summary>
  [Test]
  [Category("Conformance")]
  public void SomethingElseDrawsTheSamePixels() {
    var source = _Gradient(37, 11);
    var directory = Directory.CreateTempSubdirectory("svg");
    try {
      var drawing = Path.Combine(directory.FullName, "picture.svg");
      var rendered = Path.Combine(directory.FullName, "rendered.ppm");
      File.WriteAllBytes(drawing, SvgWriter.ToBytes(SvgFile.FromRawImage(source)));

      using var convert = ExternalTool.StartOrIgnore("magick", $"\"{drawing}\" -depth 8 \"{rendered}\"");

      var complaint = convert.StandardError.ReadToEnd();
      convert.WaitForExit();
      if (convert.ExitCode != 0 || !File.Exists(rendered))
        Assert.Ignore($"ImageMagick would not render an SVG here: {complaint.Trim()}");

      var ppm = File.ReadAllBytes(rendered);
      var header = Encoding.ASCII.GetString(ppm, 0, Math.Min(32, ppm.Length));
      var pixels = ppm[^(37 * 11 * 3)..];

      Assert.Multiple(() => {
        Assert.That(header, Does.StartWith("P6"));
        Assert.That(header, Does.Contain("37 11"));
        Assert.That(pixels, Is.EqualTo(source.PixelData), "and every pixel is the one that went in");
      });
    } finally {
      try { directory.Delete(recursive: true); } catch { /* best effort */ }
    }
  }
}
