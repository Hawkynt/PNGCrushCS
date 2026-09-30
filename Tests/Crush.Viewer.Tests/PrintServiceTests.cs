using System.IO;
using System.Text;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;

namespace Crush.Viewer.Tests;

/// <summary>The page the print command hands the platform; nothing here is actually printed.</summary>
[TestFixture]
public sealed class PrintServiceTests {

  [Test]
  public void TheRegistry_HasTheWriterPrintingReliesOn() {
    // Whether a spooler or an opener exists depends on the machine; the PDF writer must not.
    Assert.That(PrintService.Unavailable, Is.Null.Or.Not.Contain(".pdf"));
  }

  [TestCase(PixelFormat.Bgra32)]
  [TestCase(PixelFormat.Indexed8)]
  public void WriteDocument_ProducesAPdfThatReadsBackAsThePicture(PixelFormat format) {
    var picture = ViewerTestPictures.Flat(40, 24, 30, 120, 200);
    if (format == PixelFormat.Indexed8)
      picture = PixelConverter.Convert(picture, PixelFormat.Indexed8);

    var (document, error) = PrintService.WriteDocument(picture, "print test.png");
    Assert.That(error, Is.Null);
    Assert.That(document, Is.Not.Null);

    try {
      var bytes = File.ReadAllBytes(document!.FullName);
      Assert.That(Encoding.ASCII.GetString(bytes, 0, 5), Is.EqualTo("%PDF-"));
      Assert.That(document.Extension, Is.EqualTo(".pdf"));
      Assert.That(document.Name, Does.StartWith("print test-"));

      var read = FormatRegistry.Read(document);
      Assert.That(read, Is.Not.Null);
      Assert.That(read!.Width, Is.EqualTo(40));
      Assert.That(read.Height, Is.EqualTo(24));
    } finally {
      document!.Delete();
    }
  }
}
