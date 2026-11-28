using System;

namespace FileFormat.LuraDocument.Tests;

/// <summary>Decodes held to LuraTech's own decoder, on files from LuraTech's own encoder and from this package's.</summary>
/// <remarks>
/// <para>
/// The oracle is IrfanView's LuraDocument plug-in, <c>LDF.DLL</c> 3.3.7 (2012, SHA-256 <c>bc4ab111…d255</c>), which is
/// built on LuraTech's SDK and both writes and reads the format. It ran under Wine, driven through its exported
/// <c>SaveLDF</c>, <c>AddLDF_Page</c> and <c>ReadLDF</c> by a small harness. Every <c>*.expected.*</c> fixture is what
/// <c>ReadLDF</c> returned for the <c>.ldf</c> of the same name: 24-bit colour, 8-bit grey, or a 1-bit page written
/// here as 0 and 255. The plug-in is not redistributable, so its output is committed rather than run on CI.
/// </para>
/// <para>
/// The <c>luratech-*</c> files come from the plug-in's encoder in each of its five modes (full MRC, MRC at another
/// quality with a thumbnail, text, photo, mask only), from colour, grey and black-and-white pictures, at an odd size,
/// and as a three-page document. The <c>crafted-*</c> files are <c>luratech-colour-mrc</c> with one layer unlinked
/// from the directory chain. The <c>writer-*</c> files are this package's writer's output.
/// </para>
/// </remarks>
[TestFixture]
public sealed class LuraDocumentOracleTests {

  [TestCase("luratech-colour-mrc", "ppm", TestName = "LuraTech: colour page, mask with foreground and background")]
  [TestCase("luratech-colour-mrc-thumbnail", "ppm", TestName = "LuraTech: colour page at another quality, with a thumbnail")]
  [TestCase("luratech-colour-text", "ppm", TestName = "LuraTech: colour page in text mode")]
  [TestCase("luratech-colour-photo", "ppm", TestName = "LuraTech: colour page as one full-size background")]
  [TestCase("luratech-colour-maskonly", "pgm", TestName = "LuraTech: colour page kept as its mask alone")]
  [TestCase("luratech-grey-mrc", "pgm", TestName = "LuraTech: grey page, mask with grey layers")]
  [TestCase("luratech-grey-photo", "pgm", TestName = "LuraTech: grey page as one full-size background")]
  [TestCase("luratech-bilevel", "pgm", TestName = "LuraTech: black-and-white page")]
  [TestCase("luratech-odd-size", "ppm", TestName = "LuraTech: 37x23 page, layers enlarged past the page edge")]
  [TestCase("crafted-no-background", "ppm", TestName = "Crafted: no background layer, so white behind the mask")]
  [TestCase("crafted-no-foreground", "pgm", TestName = "Crafted: no foreground layer, so the mask alone")]
  [TestCase("writer-colour", "ppm", TestName = "Writer: colour")]
  [TestCase("writer-grey", "pgm", TestName = "Writer: grey")]
  [TestCase("writer-bilevel", "pgm", TestName = "Writer: black and white")]
  [Category("Integration")]
  public void DecodesExactlyAsTheVendorDecoderDoes(string name, string extension) {
    var file = LuraDocumentReader.FromBytes(LuraDocumentFixtures.Bytes(name + ".ldf"));
    var expected = LuraDocumentFixtures.Pnm(LuraDocumentFixtures.Bytes($"{name}.expected.{extension}"));
    Assert.That(file.Pages, Has.Count.EqualTo(1));
    var page = file.Pages[0];
    Assert.Multiple(() => {
      Assert.That(page.Width, Is.EqualTo(expected.Width));
      Assert.That(page.Height, Is.EqualTo(expected.Height));
      Assert.That(page.BytesPerPixel, Is.EqualTo(expected.Channels));
      Assert.That(LuraDocumentFixtures.AsDecoded(page), Is.EqualTo(expected.Pixels));
    });
  }

  [TestCase("luratech-multipage", "pgm", "pgm", "ppm", TestName = "LuraTech: three pages (black and white, grey photo, colour MRC)")]
  [TestCase("writer-multipage", "pgm", "ppm", "pgm", TestName = "Writer: three pages (black and white, colour, grey)")]
  [Category("Integration")]
  public void EveryPageDecodesExactlyAsTheVendorDecoderDoes(string name, params string[] extensions) {
    var file = LuraDocumentReader.FromBytes(LuraDocumentFixtures.Bytes(name + ".ldf"));
    Assert.That(file.Pages, Has.Count.EqualTo(extensions.Length));
    for (var index = 0; index < extensions.Length; ++index) {
      var expected = LuraDocumentFixtures.Pnm(LuraDocumentFixtures.Bytes($"{name}.p{index}.expected.{extensions[index]}"));
      var page = file.Pages[index];
      Assert.Multiple(() => {
        Assert.That(page.Width, Is.EqualTo(expected.Width), $"page {index}");
        Assert.That(page.Height, Is.EqualTo(expected.Height), $"page {index}");
        Assert.That(LuraDocumentFixtures.AsDecoded(page), Is.EqualTo(expected.Pixels), $"page {index}");
      });
    }
  }

  [Test]
  [Category("Unit")]
  public void LayersAreReportedAsTheyAreStored() {
    var mrc = LuraDocumentReader.FromBytes(LuraDocumentFixtures.Bytes("luratech-colour-mrc.ldf")).Pages[0];
    var photo = LuraDocumentReader.FromBytes(LuraDocumentFixtures.Bytes("luratech-colour-photo.ldf")).Pages[0];
    Assert.Multiple(() => {
      Assert.That(mrc.HasMask, Is.True);
      Assert.That(mrc.ForegroundSize, Is.EqualTo((32, 24)));
      Assert.That(mrc.BackgroundSize, Is.EqualTo((32, 24)));
      Assert.That(mrc.HorizontalDpi, Is.EqualTo(300));
      Assert.That(photo.HasMask, Is.False);
      Assert.That(photo.ForegroundSize, Is.Null);
      Assert.That(photo.BackgroundSize, Is.EqualTo((64, 48)));
    });
  }
}
