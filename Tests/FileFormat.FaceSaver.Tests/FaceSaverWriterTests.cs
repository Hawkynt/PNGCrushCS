using System;
using System.Text;
using FileFormat.FaceSaver;

namespace FileFormat.FaceSaver.Tests;

[TestFixture]
public sealed class FaceSaverWriterTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => FaceSaverWriter.ToBytes(null!));

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsPicDataHeader() {
    var file = new FaceSaverFile { Width = 4, Height = 3, PixelData = new byte[12] };
    var bytes = FaceSaverWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);
    Assert.That(text, Does.Contain("PicData: 4 3 8"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsImageHeader() {
    var file = new FaceSaverFile { Width = 4, Height = 3, PixelData = new byte[12] };
    var bytes = FaceSaverWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);
    Assert.That(text, Does.Contain("Image: 4 3 8"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsBlankLineSeparator() {
    var file = new FaceSaverFile { Width = 2, Height = 2, PixelData = new byte[4] };
    var bytes = FaceSaverWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);
    Assert.That(text, Does.Contain("\n\n"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ContainsHexData() {
    var file = new FaceSaverFile {
      Width = 1, Height = 1, PixelData = [0xAB]
    };
    var bytes = FaceSaverWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);
    // After blank line separator, should have hex "ab"
    var dataStart = text.IndexOf("\n\n", StringComparison.Ordinal) + 2;
    var data = text[dataStart..].Trim();
    Assert.That(data, Is.EqualTo("ab"));
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_PreservesHeaderFields() {
    var file = new FaceSaverFile {
      Width = 2, Height = 2,
      FirstName = "Jane",
      LastName = "Smith",
      Email = "jane@test.com",
      PixelData = new byte[4],
    };
    var bytes = FaceSaverWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.Multiple(() => {
      Assert.That(text, Does.Contain("FirstName: Jane"));
      Assert.That(text, Does.Contain("LastName: Smith"));
      Assert.That(text, Does.Contain("E-mail: jane@test.com"));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_ImageFieldUsesCustomDimensions() {
    var file = new FaceSaverFile {
      Width = 108, Height = 96,
      ImageWidth = 96, ImageHeight = 96,
      PixelData = new byte[108 * 96],
    };
    var bytes = FaceSaverWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);

    Assert.Multiple(() => {
      Assert.That(text, Does.Contain("PicData: 108 96 8"));
      Assert.That(text, Does.Contain("Image: 96 96 8"));
    });
  }

  [Test]
  [Category("Unit")]
  public void ToBytes_LineWrapsAt30Pairs() {
    // 31 pixels = should wrap after 30 hex pairs
    var file = new FaceSaverFile {
      Width = 31, Height = 1, PixelData = new byte[31]
    };
    var bytes = FaceSaverWriter.ToBytes(file);
    var text = Encoding.ASCII.GetString(bytes);
    var dataStart = text.IndexOf("\n\n", StringComparison.Ordinal) + 2;
    var dataLines = text[dataStart..].TrimEnd('\n').Split('\n');
    Assert.Multiple(() => {
      Assert.That(dataLines[0], Has.Length.EqualTo(60)); // 30 pairs * 2 chars
      Assert.That(dataLines[1], Has.Length.EqualTo(2)); // 1 pair
    });
  }
}
