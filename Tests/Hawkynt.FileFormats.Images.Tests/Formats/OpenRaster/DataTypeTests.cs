using System;
using FileFormat.OpenRaster;

namespace FileFormat.OpenRaster.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void _DefaultPixelData_IsNull() {
    var file = new OpenRasterFile { Width = 1, Height = 1 };
    Assert.That(file.PixelData, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void OpenRasterFile_DefaultLayers_IsNull() {
    var file = new OpenRasterFile { Width = 1, Height = 1 };
    Assert.That(file.Layers, Is.Null);
  }

  [Test]
  [Category("Unit")]
  public void OpenRasterLayer_DefaultOpacity_IsOne() {
    var layer = new OpenRasterLayer();
    Assert.That(layer.Opacity, Is.EqualTo(1.0f));
  }

  [Test]
  [Category("Unit")]
  public void OpenRasterLayer_DefaultVisibility_IsTrue() {
    var layer = new OpenRasterLayer();
    Assert.That(layer.Visibility, Is.True);
  }

  [Test]
  [Category("Unit")]
  public void OpenRasterLayer_DefaultName_IsEmpty() {
    var layer = new OpenRasterLayer();
    Assert.That(layer.Name, Is.EqualTo(""));
  }

  [Test]
  [Category("Unit")]
  public void OpenRasterLayer_DefaultPixelData_IsEmptyArray() {
    var layer = new OpenRasterLayer();
    Assert.That(layer.PixelData, Is.Not.Null);
    Assert.That(layer.PixelData, Has.Length.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void OpenRasterLayer_InitProperties_RoundTrip() {
    var pixels = new byte[] { 1, 2, 3, 4 };
    var layer = new OpenRasterLayer {
      Name = "Test",
      X = 10,
      Y = 20,
      Width = 1,
      Height = 1,
      Opacity = 0.7f,
      Visibility = false,
      PixelData = pixels
    };

    Assert.That(layer.Name, Is.EqualTo("Test"));
    Assert.That(layer.X, Is.EqualTo(10));
    Assert.That(layer.Y, Is.EqualTo(20));
    Assert.That(layer.Width, Is.EqualTo(1));
    Assert.That(layer.Height, Is.EqualTo(1));
    Assert.That(layer.Opacity, Is.EqualTo(0.7f));
    Assert.That(layer.Visibility, Is.False);
    Assert.That(layer.PixelData, Is.SameAs(pixels));
  }

  [Test]
  [Category("Unit")]
  public void OpenRasterFile_InitProperties_RoundTrip() {
    var pixels = new byte[] { 1, 2, 3, 4 };
    var layers = new[] {
      new OpenRasterLayer { Name = "L1" }
    };

    var file = new OpenRasterFile {
      Width = 100,
      Height = 200,
      PixelData = pixels,
      Layers = layers
    };

    Assert.That(file.Width, Is.EqualTo(100));
    Assert.That(file.Height, Is.EqualTo(200));
    Assert.That(file.PixelData, Is.SameAs(pixels));
    Assert.That(file.Layers, Has.Count.EqualTo(1));
    Assert.That(file.Layers[0].Name, Is.EqualTo("L1"));
  }
}
