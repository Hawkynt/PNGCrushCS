using System;
using FileFormat.Core;
using FileFormat.Xpm;

namespace Hawkynt.FileFormats.Images.Tests;

/// <summary>
/// A "personal icon" is an XPM — the name says what the file is for, not what it is.
/// </summary>
/// <remarks>
/// The text below is exactly what ImageMagick emits for <c>picon:</c>, magic comment and all, so
/// reading it proves the extension belongs to this format rather than needing one of its own.
/// </remarks>
[TestFixture]
public sealed class PiconTests {

  private const string Reference = "/* XPM */\nstatic const char *p[] = {\n/* columns rows colors chars-per-pixel */\n\"4 2 8 1\",\n\"  c black\",\n\". c gray13\",\n\"X c gray27\",\n\"o c gray40\",\n\"O c gray53\",\n\"+ c gray67\",\n\"@ c gray93\",\n\"# c white\",\n/* pixels */\n\" XO#\",\n\".o+@\"\n};\n";

  [Test]
  [Category("Unit")]
  public void ReferencePicon_ReadsAsAnXpm() {
    var bytes = System.Text.Encoding.ASCII.GetBytes(Reference);

    var image = XpmFile.ToRawImage(XpmReader.FromBytes(bytes));

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(4));
      Assert.That(image.Height, Is.EqualTo(2));
    });
  }
}
