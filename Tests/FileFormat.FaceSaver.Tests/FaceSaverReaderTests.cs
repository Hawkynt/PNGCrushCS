using System;
using System.IO;
using System.Text;
using FileFormat.FaceSaver;

namespace FileFormat.FaceSaver.Tests;

[TestFixture]
public sealed class FaceSaverReaderTests {

  [Test]
  [Category("Unit")]
  public void FromFile_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => FaceSaverReader.FromFile(null!));

  [Test]
  [Category("Unit")]
  public void FromFile_Missing_Throws()
    => Assert.Throws<FileNotFoundException>(() => FaceSaverReader.FromFile(new FileInfo("nonexistent.face")));

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => FaceSaverReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_TooSmall_Throws()
    => Assert.Throws<InvalidDataException>(() => FaceSaverReader.FromBytes(new byte[5]));

  [Test]
  [Category("Unit")]
  public void FromStream_Null_Throws()
    => Assert.Throws<ArgumentNullException>(() => FaceSaverReader.FromStream(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_NoPicData_Throws() {
    var text = "FirstName: John\nLastName: Doe\n\nff\n";
    var data = Encoding.ASCII.GetBytes(text);
    Assert.Throws<InvalidDataException>(() => FaceSaverReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ValidMinimal_ParsesDimensions() {
    var sb = new StringBuilder();
    sb.Append("PicData: 2 2 8\n");
    sb.Append("Image: 2 2 8\n");
    sb.Append('\n');
    // 4 pixels, each 2 hex chars = "00112233"
    sb.Append("00112233\n");

    var data = Encoding.ASCII.GetBytes(sb.ToString());
    var file = FaceSaverReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(2));
      Assert.That(file.Height, Is.EqualTo(2));
      Assert.That(file.BitsPerPixel, Is.EqualTo(8));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ParsesHeaderFields() {
    var sb = new StringBuilder();
    sb.Append("FirstName: John\n");
    sb.Append("LastName: Doe\n");
    sb.Append("E-mail: john@example.com\n");
    sb.Append("Telephone: 555-1234\n");
    sb.Append("Company: Acme\n");
    sb.Append("Address1: 123 Main St\n");
    sb.Append("Address2: Suite 100\n");
    sb.Append("CityStateZip: Springfield IL 62701\n");
    sb.Append("Date: 1989-01-15\n");
    sb.Append("PicData: 2 2 8\n");
    sb.Append("Image: 2 2 8\n");
    sb.Append('\n');
    sb.Append("00000000\n");

    var data = Encoding.ASCII.GetBytes(sb.ToString());
    var file = FaceSaverReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(file.FirstName, Is.EqualTo("John"));
      Assert.That(file.LastName, Is.EqualTo("Doe"));
      Assert.That(file.Email, Is.EqualTo("john@example.com"));
      Assert.That(file.Telephone, Is.EqualTo("555-1234"));
      Assert.That(file.Company, Is.EqualTo("Acme"));
      Assert.That(file.Address1, Is.EqualTo("123 Main St"));
      Assert.That(file.Address2, Is.EqualTo("Suite 100"));
      Assert.That(file.CityStateZip, Is.EqualTo("Springfield IL 62701"));
      Assert.That(file.Date, Is.EqualTo("1989-01-15"));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_PixelData_BottomToTop() {
    // 2x2 image: file has bottom row first, then top row
    // File hex: "FF00" (bottom row: 0xFF, 0x00) then "0AFA" (top row: 0x0A, 0xFA)
    // After flip: top row = [0x0A, 0xFA], bottom row = [0xFF, 0x00]
    var sb = new StringBuilder();
    sb.Append("PicData: 2 2 8\n");
    sb.Append("Image: 2 2 8\n");
    sb.Append('\n');
    sb.Append("FF000AFA\n");

    var data = Encoding.ASCII.GetBytes(sb.ToString());
    var file = FaceSaverReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(file.PixelData[0], Is.EqualTo(0x0A), "top-left");
      Assert.That(file.PixelData[1], Is.EqualTo(0xFA), "top-right");
      Assert.That(file.PixelData[2], Is.EqualTo(0xFF), "bottom-left");
      Assert.That(file.PixelData[3], Is.EqualTo(0x00), "bottom-right");
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ImageField_Parsed() {
    var sb = new StringBuilder();
    sb.Append("PicData: 108 96 8\n");
    sb.Append("Image: 96 96 8\n");
    sb.Append('\n');
    // Provide enough hex data for 108x96 pixels = 10368 bytes = 20736 hex chars
    sb.Append(new string('0', 108 * 96 * 2));
    sb.Append('\n');

    var data = Encoding.ASCII.GetBytes(sb.ToString());
    var file = FaceSaverReader.FromBytes(data);

    Assert.Multiple(() => {
      Assert.That(file.Width, Is.EqualTo(108));
      Assert.That(file.Height, Is.EqualTo(96));
      Assert.That(file.ImageWidth, Is.EqualTo(96));
      Assert.That(file.ImageHeight, Is.EqualTo(96));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromStream_ValidData_Parses() {
    var sb = new StringBuilder();
    sb.Append("PicData: 2 2 8\n");
    sb.Append("Image: 2 2 8\n");
    sb.Append('\n');
    sb.Append("aabbccdd\n");

    var bytes = Encoding.ASCII.GetBytes(sb.ToString());
    using var stream = new MemoryStream(bytes);
    var file = FaceSaverReader.FromStream(stream);

    Assert.That(file.Width, Is.EqualTo(2));
    Assert.That(file.Height, Is.EqualTo(2));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_HexCaseInsensitive() {
    var sb = new StringBuilder();
    sb.Append("PicData: 1 2 8\n");
    sb.Append("Image: 1 2 8\n");
    sb.Append('\n');
    sb.Append("aB\n");
    sb.Append("Cd\n");

    var data = Encoding.ASCII.GetBytes(sb.ToString());
    var file = FaceSaverReader.FromBytes(data);

    Assert.Multiple(() => {
      // File: bottom row first = 0xAB, top row = 0xCD
      // After flip: top = 0xCD, bottom = 0xAB
      Assert.That(file.PixelData[0], Is.EqualTo(0xCD));
      Assert.That(file.PixelData[1], Is.EqualTo(0xAB));
    });
  }
}
