using System;
using FileFormat.Jpeg2000;

namespace FileFormat.Jpeg2000.Tests;

[TestFixture]
public sealed class Jp2WaveletTests {

  [Test]
  [Category("Unit")]
  public void Forward1D_Inverse1D_RoundTrip_PreservesSignal() {
    var data = new[] { 10, 20, 30, 40, 50, 60, 70, 80 };
    var length = data.Length;
    var low = new int[(length + 1) / 2];
    var high = new int[length / 2];

    Jp2Wavelet.Forward1D(data, length, low, high);

    var output = new int[length];
    Jp2Wavelet.Inverse1D(low, high, length, output);

    Assert.That(output, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void Forward1D_Inverse1D_OddLength_PreservesSignal() {
    var data = new[] { 5, 15, 25, 35, 45 };
    var length = data.Length;
    var low = new int[(length + 1) / 2];
    var high = new int[length / 2];

    Jp2Wavelet.Forward1D(data, length, low, high);

    var output = new int[length];
    Jp2Wavelet.Inverse1D(low, high, length, output);

    Assert.That(output, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void Forward1D_Inverse1D_Length2_PreservesSignal() {
    var data = new[] { 100, 200 };
    var length = data.Length;
    var low = new int[1];
    var high = new int[1];

    Jp2Wavelet.Forward1D(data, length, low, high);

    var output = new int[length];
    Jp2Wavelet.Inverse1D(low, high, length, output);

    Assert.That(output, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void Forward1D_Inverse1D_AllZeros_PreservesSignal() {
    var data = new[] { 0, 0, 0, 0 };
    var length = data.Length;
    var low = new int[2];
    var high = new int[2];

    Jp2Wavelet.Forward1D(data, length, low, high);

    var output = new int[length];
    Jp2Wavelet.Inverse1D(low, high, length, output);

    Assert.That(output, Is.EqualTo(data));
  }

  [Test]
  [Category("Unit")]
  public void Forward1D_ConstantSignal_HighPassIsZero() {
    var data = new[] { 42, 42, 42, 42, 42, 42 };
    var length = data.Length;
    var low = new int[(length + 1) / 2];
    var high = new int[length / 2];

    Jp2Wavelet.Forward1D(data, length, low, high);

    foreach (var h in high)
      Assert.That(h, Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void Forward2D_Inverse2D_RoundTrip_PreservesPlane() {
    var plane = new int[4, 4];
    for (var y = 0; y < 4; ++y)
      for (var x = 0; x < 4; ++x)
        plane[y, x] = y * 10 + x;

    var original = (int[,])plane.Clone();

    Jp2Wavelet.Forward2D(plane, 4, 4);
    Jp2Wavelet.Inverse2D(plane, 4, 4);

    for (var y = 0; y < 4; ++y)
      for (var x = 0; x < 4; ++x)
        Assert.That(plane[y, x], Is.EqualTo(original[y, x]), $"Mismatch at [{y},{x}]");
  }

  [Test]
  [Category("Unit")]
  public void Forward2D_Inverse2D_OddDimensions_PreservesPlane() {
    var plane = new int[3, 5];
    for (var y = 0; y < 3; ++y)
      for (var x = 0; x < 5; ++x)
        plane[y, x] = (y + 1) * (x + 1);

    var original = (int[,])plane.Clone();

    Jp2Wavelet.Forward2D(plane, 5, 3);
    Jp2Wavelet.Inverse2D(plane, 5, 3);

    for (var y = 0; y < 3; ++y)
      for (var x = 0; x < 5; ++x)
        Assert.That(plane[y, x], Is.EqualTo(original[y, x]), $"Mismatch at [{y},{x}]");
  }

  [Test]
  [Category("Unit")]
  public void Forward2D_Inverse2D_AllZeros_PreservesPlane() {
    var plane = new int[4, 4];
    Jp2Wavelet.Forward2D(plane, 4, 4);
    Jp2Wavelet.Inverse2D(plane, 4, 4);

    for (var y = 0; y < 4; ++y)
      for (var x = 0; x < 4; ++x)
        Assert.That(plane[y, x], Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ForwardMultiLevel_InverseMultiLevel_RoundTrip_PreservesPlane() {
    var plane = new int[8, 8];
    for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x)
        plane[y, x] = (y * 8 + x) * 3;

    var original = (int[,])plane.Clone();

    Jp2Wavelet.ForwardMultiLevel(plane, 8, 8, 3);
    Jp2Wavelet.InverseMultiLevel(plane, 8, 8, 3);

    for (var y = 0; y < 8; ++y)
      for (var x = 0; x < 8; ++x)
        Assert.That(plane[y, x], Is.EqualTo(original[y, x]), $"Mismatch at [{y},{x}]");
  }

  [Test]
  [Category("Unit")]
  public void ForwardMultiLevel_InverseMultiLevel_SingleLevel_PreservesPlane() {
    var plane = new int[4, 4];
    for (var y = 0; y < 4; ++y)
      for (var x = 0; x < 4; ++x)
        plane[y, x] = y * 40 + x * 10;

    var original = (int[,])plane.Clone();

    Jp2Wavelet.ForwardMultiLevel(plane, 4, 4, 1);
    Jp2Wavelet.InverseMultiLevel(plane, 4, 4, 1);

    for (var y = 0; y < 4; ++y)
      for (var x = 0; x < 4; ++x)
        Assert.That(plane[y, x], Is.EqualTo(original[y, x]), $"Mismatch at [{y},{x}]");
  }

  [Test]
  [Category("Unit")]
  public void ForwardMultiLevel_InverseMultiLevel_OddDimensions_PreservesPlane() {
    var w = 7;
    var h = 5;
    var plane = new int[h, w];
    for (var y = 0; y < h; ++y)
      for (var x = 0; x < w; ++x)
        plane[y, x] = y * w + x;

    var original = (int[,])plane.Clone();

    Jp2Wavelet.ForwardMultiLevel(plane, w, h, 2);
    Jp2Wavelet.InverseMultiLevel(plane, w, h, 2);

    for (var y = 0; y < h; ++y)
      for (var x = 0; x < w; ++x)
        Assert.That(plane[y, x], Is.EqualTo(original[y, x]), $"Mismatch at [{y},{x}]");
  }

  [Test]
  [Category("Unit")]
  public void ForwardMultiLevel_InverseMultiLevel_LargerImage_PreservesPlane() {
    var w = 16;
    var h = 16;
    var plane = new int[h, w];
    var rng = new Random(42);
    for (var y = 0; y < h; ++y)
      for (var x = 0; x < w; ++x)
        plane[y, x] = rng.Next(256);

    var original = (int[,])plane.Clone();

    Jp2Wavelet.ForwardMultiLevel(plane, w, h, 3);
    Jp2Wavelet.InverseMultiLevel(plane, w, h, 3);

    for (var y = 0; y < h; ++y)
      for (var x = 0; x < w; ++x)
        Assert.That(plane[y, x], Is.EqualTo(original[y, x]), $"Mismatch at [{y},{x}]");
  }

  [Test]
  [Category("Unit")]
  public void Forward1D_Inverse1D_NegativeValues_PreservesSignal() {
    var data = new[] { -10, 20, -30, 40, -50, 60 };
    var length = data.Length;
    var low = new int[(length + 1) / 2];
    var high = new int[length / 2];

    Jp2Wavelet.Forward1D(data, length, low, high);

    var output = new int[length];
    Jp2Wavelet.Inverse1D(low, high, length, output);

    Assert.That(output, Is.EqualTo(data));
  }
}
