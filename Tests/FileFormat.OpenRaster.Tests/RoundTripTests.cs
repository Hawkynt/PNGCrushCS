using System;
using FileFormat.OpenRaster;

namespace FileFormat.OpenRaster.Tests;

[TestFixture]
public sealed class RoundTripTests {

  [Test]
  [Category("Integration")]
  public void RoundTrip_SingleLayer_PreservesPixelData() {
    var width = 4;
    var height = 4;
    var rgba = _BuildGradientRgba(width, height);

    var original = new OpenRasterFile {
      Width = width,
      Height = height,
      PixelData = rgba,
      Layers = [
        new OpenRasterLayer {
          Name = "Background",
          Width = width,
          Height = height,
          PixelData = rgba
        }
      ]
    };

    var bytes = OpenRasterWriter.ToBytes(original);
    var restored = OpenRasterReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
    Assert.That(restored.PixelData, Is.EqualTo(original.PixelData));
    Assert.That(restored.Layers, Has.Count.EqualTo(1));
    Assert.That(restored.Layers[0].PixelData, Is.EqualTo(original.Layers[0].PixelData));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_MultiLayer_PreservesAllLayers() {
    var width = 3;
    var height = 3;
    var layer0Data = _BuildGradientRgba(width, height);
    var layer1Data = new byte[width * height * 4];
    for (var i = 0; i < layer1Data.Length; ++i)
      layer1Data[i] = (byte)(255 - i % 256);

    var merged = _BuildGradientRgba(width, height);

    var original = new OpenRasterFile {
      Width = width,
      Height = height,
      PixelData = merged,
      Layers = [
        new OpenRasterLayer {
          Name = "Layer A",
          Width = width,
          Height = height,
          PixelData = layer0Data
        },
        new OpenRasterLayer {
          Name = "Layer B",
          Width = width,
          Height = height,
          PixelData = layer1Data
        }
      ]
    };

    var bytes = OpenRasterWriter.ToBytes(original);
    var restored = OpenRasterReader.FromBytes(bytes);

    Assert.That(restored.Layers, Has.Count.EqualTo(2));
    Assert.That(restored.Layers[0].Name, Is.EqualTo("Layer A"));
    Assert.That(restored.Layers[1].Name, Is.EqualTo("Layer B"));
    Assert.That(restored.Layers[0].PixelData, Is.EqualTo(layer0Data));
    Assert.That(restored.Layers[1].PixelData, Is.EqualTo(layer1Data));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LayerPosition_Preserved() {
    var canvasW = 8;
    var canvasH = 8;
    var layerW = 4;
    var layerH = 4;
    var layerData = _BuildGradientRgba(layerW, layerH);
    var merged = new byte[canvasW * canvasH * 4];

    var original = new OpenRasterFile {
      Width = canvasW,
      Height = canvasH,
      PixelData = merged,
      Layers = [
        new OpenRasterLayer {
          Name = "Offset",
          X = 3,
          Y = 5,
          Width = layerW,
          Height = layerH,
          PixelData = layerData
        }
      ]
    };

    var bytes = OpenRasterWriter.ToBytes(original);
    var restored = OpenRasterReader.FromBytes(bytes);

    Assert.That(restored.Layers[0].X, Is.EqualTo(3));
    Assert.That(restored.Layers[0].Y, Is.EqualTo(5));
    Assert.That(restored.Layers[0].Width, Is.EqualTo(layerW));
    Assert.That(restored.Layers[0].Height, Is.EqualTo(layerH));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LayerOpacity_Preserved() {
    var width = 2;
    var height = 2;
    var rgba = new byte[width * height * 4];

    var original = new OpenRasterFile {
      Width = width,
      Height = height,
      PixelData = rgba,
      Layers = [
        new OpenRasterLayer {
          Name = "SemiTransparent",
          Width = width,
          Height = height,
          Opacity = 0.5f,
          PixelData = rgba
        }
      ]
    };

    var bytes = OpenRasterWriter.ToBytes(original);
    var restored = OpenRasterReader.FromBytes(bytes);

    Assert.That(restored.Layers[0].Opacity, Is.EqualTo(0.5f).Within(0.01f));
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_LayerVisibility_Preserved() {
    var width = 2;
    var height = 2;
    var rgba = new byte[width * height * 4];

    var original = new OpenRasterFile {
      Width = width,
      Height = height,
      PixelData = rgba,
      Layers = [
        new OpenRasterLayer {
          Name = "Hidden",
          Width = width,
          Height = height,
          Visibility = false,
          PixelData = rgba
        }
      ]
    };

    var bytes = OpenRasterWriter.ToBytes(original);
    var restored = OpenRasterReader.FromBytes(bytes);

    Assert.That(restored.Layers[0].Visibility, Is.False);
  }

  [Test]
  [Category("Integration")]
  public void RoundTrip_CanvasDimensions_Preserved() {
    var width = 123;
    var height = 456;
    var rgba = _BuildGradientRgba(width, height);

    var original = new OpenRasterFile {
      Width = width,
      Height = height,
      PixelData = rgba,
      Layers = [
        new OpenRasterLayer {
          Name = "Full",
          Width = width,
          Height = height,
          PixelData = rgba
        }
      ]
    };

    var bytes = OpenRasterWriter.ToBytes(original);
    var restored = OpenRasterReader.FromBytes(bytes);

    Assert.That(restored.Width, Is.EqualTo(width));
    Assert.That(restored.Height, Is.EqualTo(height));
  }

  private static byte[] _BuildGradientRgba(int width, int height) {
    var data = new byte[width * height * 4];
    for (var i = 0; i < width * height; ++i) {
      data[i * 4] = (byte)(i * 7 % 256);
      data[i * 4 + 1] = (byte)(i * 13 % 256);
      data[i * 4 + 2] = (byte)(i * 23 % 256);
      data[i * 4 + 3] = 255;
    }

    return data;
  }
}
