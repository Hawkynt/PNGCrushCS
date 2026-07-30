using System;
using FileFormat.Xwd;

namespace FileFormat.Xwd.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void XwdVisualClass_HasExpectedValues() {
    Assert.That((uint)XwdVisualClass.StaticGray, Is.EqualTo(0));
    Assert.That((uint)XwdVisualClass.GrayScale, Is.EqualTo(1));
    Assert.That((uint)XwdVisualClass.StaticColor, Is.EqualTo(2));
    Assert.That((uint)XwdVisualClass.PseudoColor, Is.EqualTo(3));
    Assert.That((uint)XwdVisualClass.TrueColor, Is.EqualTo(4));
    Assert.That((uint)XwdVisualClass.DirectColor, Is.EqualTo(5));

    var values = Enum.GetValues<XwdVisualClass>();
    Assert.That(values, Has.Length.EqualTo(6));
  }

  [Test]
  [Category("Unit")]
  public void XwdPixmapFormat_HasExpectedValues() {
    Assert.That((uint)XwdPixmapFormat.XYBitmap, Is.EqualTo(0));
    Assert.That((uint)XwdPixmapFormat.XYPixmap, Is.EqualTo(1));
    Assert.That((uint)XwdPixmapFormat.ZPixmap, Is.EqualTo(2));

    var values = Enum.GetValues<XwdPixmapFormat>();
    Assert.That(values, Has.Length.EqualTo(3));
  }

  [Test]
  [Category("Unit")]
  public void XwdFile_DefaultPixelData_IsNull() {
    var file = new XwdFile { Width = 1, Height = 1 };
    Assert.That(file.PixelData, Is.Null);
    Assert.That(file.WindowName, Is.Null);
    Assert.That(file.Colormap, Is.Null);
  }
}
