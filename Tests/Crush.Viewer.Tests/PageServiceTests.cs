using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace Crush.Viewer.Tests;

/// <summary>Reading the pages out of a multi-page file, and writing each one to its own file.</summary>
[TestFixture]
public sealed class PageServiceTests {

  private static FileInfo _TwoPageIcon(ScratchFolder scratch)
    => ViewerTestPictures.WriteIcon(
      scratch.Directory,
      "pair.ico",
      [
        ViewerTestPictures.PngBytes(16, 16, 0, 0, 255),
        ViewerTestPictures.PngBytes(32, 32, 255, 0, 0),
      ],
      [(16, 16), (32, 32)]);

  [Test]
  public void PageCount_AnIconHoldingTwoPictures_SaysTwo() {
    using var scratch = new ScratchFolder("ico-count");
    var icon = _TwoPageIcon(scratch);
    var entry = FormatRegistry.GetEntry(FormatRegistry.DetectFromFile(icon));

    Assert.That(entry?.Name, Is.Not.Null);
    Assert.That(PageService.PageCount(icon, entry), Is.EqualTo(2));
  }

  [Test]
  public void PageCount_AnOrdinarySinglePictureFile_SaysOne() {
    using var scratch = new ScratchFolder("png-count");
    var png = ViewerTestPictures.WritePng(scratch.Directory, "one.png", 4, 4, 0, 0, 0);
    var entry = FormatRegistry.GetEntry(FormatRegistry.DetectFromFile(png));

    Assert.That(PageService.PageCount(png, entry), Is.EqualTo(1));
  }

  [Test]
  public void LoadPage_HandsBackThePageThatWasAskedFor() {
    using var scratch = new ScratchFolder("ico-page");
    var icon = _TwoPageIcon(scratch);
    var entry = FormatRegistry.GetEntry(FormatRegistry.DetectFromFile(icon));

    var first = PageService.LoadPage(icon, entry, 0);
    var second = PageService.LoadPage(icon, entry, 1);

    Assert.That(first, Is.Not.Null);
    Assert.That(second, Is.Not.Null);
    Assert.That((first!.Width, first.Height), Is.EqualTo((16, 16)));
    Assert.That((second!.Width, second.Height), Is.EqualTo((32, 32)));
  }

  [Test]
  public void ExtractPages_WritesEveryPageToItsOwnNumberedFile() {
    using var scratch = new ScratchFolder("ico-extract");
    var icon = _TwoPageIcon(scratch);
    var entry = FormatRegistry.GetEntry(FormatRegistry.DetectFromFile(icon));
    var destination = scratch.Sub("pages");

    var results = PageService.ExtractPages(icon, entry, FormatRegistry.GetEntry(ImageFormat.Png)!, destination, overwrite: true);

    Assert.That(results.Where(r => !r.Succeeded).Select(r => r.Message), Is.Empty);
    Assert.That(results, Has.Exactly(2).Items);

    destination.Refresh();
    var written = destination.GetFiles().OrderBy(f => f.Name).ToArray();
    Assert.That(written.Select(f => f.Name), Is.EqualTo(new[] { "pair.01.png", "pair.02.png" }));

    // The pages come out in their own order and are the pictures that went in, not one of them twice.
    var sizes = written.Select(f => FormatRegistry.Read(f)!).Select(i => (i.Width, i.Height)).ToArray();
    Assert.That(sizes, Is.EqualTo(new[] { (16, 16), (32, 32) }));
  }

  [Test]
  public void ExtractPages_WhenAPageFileIsAlreadyThereAndOverwritingIsOff_RefusesThatPageOnly() {
    using var scratch = new ScratchFolder("ico-extract-refuse");
    var icon = _TwoPageIcon(scratch);
    var entry = FormatRegistry.GetEntry(FormatRegistry.DetectFromFile(icon));
    var destination = scratch.Sub("pages");
    var occupied = Path.Combine(destination.FullName, "pair.01.png");
    File.WriteAllText(occupied, "in the way");

    var results = PageService.ExtractPages(icon, entry, FormatRegistry.GetEntry(ImageFormat.Png)!, destination, overwrite: false);

    Assert.That(results, Has.Exactly(2).Items);
    Assert.That(results[0].Succeeded, Is.False);
    Assert.That(results[0].Message, Does.Contain("already there"));
    Assert.That(results[1].Succeeded, Is.True, results[1].Message);
    Assert.That(File.ReadAllText(occupied), Is.EqualTo("in the way"));
  }

  [Test]
  public void ExtractPages_ASinglePictureFile_WritesOnePage() {
    using var scratch = new ScratchFolder("png-extract");
    var png = ViewerTestPictures.WritePng(scratch.Directory, "one.png", 5, 3, 9, 8, 7);
    var entry = FormatRegistry.GetEntry(FormatRegistry.DetectFromFile(png));
    var destination = scratch.Sub("pages");

    var results = PageService.ExtractPages(png, entry, FormatRegistry.GetEntry(ImageFormat.Bmp)!, destination, overwrite: true);

    Assert.That(results, Has.Exactly(1).Items);
    Assert.That(results[0].Succeeded, Is.True, results[0].Message);
    destination.Refresh();
    Assert.That(destination.GetFiles().Single().Name, Is.EqualTo("one.01.bmp"));
  }

  [Test]
  public void AssemblyUnavailable_StaysTrueOnlyWhileNoFormatCanBeHandedSeveralPictures() {
    // The ribbon shows this as the disabled "Assemble pages…" button's tooltip. It is a claim about
    // the format layer, not about the viewer, so it has to fail the day that layer grows the
    // counterpart it names — otherwise the button stays greyed out long after the reason is gone.
    Assert.That(PageService.AssemblyUnavailable, Does.Contain("IMultiImageFileFormat"));

    var multiImage = typeof(IMultiImageFileFormat<>);
    var writesSeveral = multiImage
      .GetMethods()
      .Where(m => m.GetParameters().Any(p => typeof(System.Collections.IEnumerable).IsAssignableFrom(p.ParameterType)))
      .Select(m => m.Name)
      .ToArray();

    Assert.That(
      writesSeveral,
      Is.Empty,
      "IMultiImageFileFormat now takes a list of pictures, so 'Assemble pages…' can be built and enabled");
  }
}
