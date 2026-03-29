using System;
using System.IO;
using System.Reflection;
using FileFormat.Core;
using FileFormat.WindowsPe;

namespace FileFormat.WindowsPe.Tests;

[TestFixture]
public sealed class PeResourceFileTests {

  [Test]
  [Category("Unit")]
  public void ToBytes_AlwaysThrows_NotSupported() {
    var file = new PeResourceFile();
    var ex = Assert.Throws<TargetInvocationException>(() => _InvokeToBytes(file));
    Assert.That(ex!.InnerException, Is.InstanceOf<NotSupportedException>());
  }

  [Test]
  [Category("Unit")]
  public void FromRawImage_AlwaysThrows_NotSupported() {
    var raw = new RawImage { Width = 2, Height = 2, Format = PixelFormat.Rgb24, PixelData = new byte[12] };
    var ex = Assert.Throws<TargetInvocationException>(() => _InvokeFromRawImage(raw));
    Assert.That(ex!.InnerException, Is.InstanceOf<NotSupportedException>());
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => PeResourceFile.ToRawImage(null!));

  [Test]
  [Category("Unit")]
  public void ToRawImage_NoResources_Throws() {
    var file = new PeResourceFile();
    Assert.Throws<InvalidOperationException>(() => PeResourceFile.ToRawImage(file));
  }

  [Test]
  [Category("Unit")]
  public void ImageCount_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => PeResourceFile.ImageCount(null!));

  [Test]
  [Category("Unit")]
  public void ImageCount_EmptyFile_ReturnsZero() {
    var file = new PeResourceFile();
    Assert.That(PeResourceFile.ImageCount(file), Is.EqualTo(0));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ByIndex_NullFile_Throws()
    => Assert.Throws<ArgumentNullException>(() => PeResourceFile.ToRawImage(null!, 0));

  [Test]
  [Category("Unit")]
  public void ToRawImage_ByIndex_OutOfRange_Throws() {
    var file = new PeResourceFile();
    Assert.Throws<ArgumentOutOfRangeException>(() => PeResourceFile.ToRawImage(file, 0));
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ByIndex_NegativeIndex_Throws() {
    var file = new PeResourceFile();
    Assert.Throws<ArgumentOutOfRangeException>(() => PeResourceFile.ToRawImage(file, -1));
  }

  [Test]
  [Category("Integration")]
  public void ImageCount_WithBitmapResource_ReturnsOne() {
    var dib = MinimalPeBuilder.CreateMinimalDib(2, 2);
    var pe = MinimalPeBuilder.BuildWithBitmap(dib);
    var file = PeResourceReader.FromBytes(pe);
    Assert.That(PeResourceFile.ImageCount(file), Is.EqualTo(1));
  }

  private static byte[] _InvokeToBytes(PeResourceFile file) {
    var map = typeof(PeResourceFile).GetInterfaceMap(typeof(IImageFileFormat<PeResourceFile>));
    foreach (var method in map.TargetMethods)
      if (method.Name.Contains("ToBytes"))
        return (byte[])method.Invoke(null, new object[] { file })!;
    throw new InvalidOperationException("ToBytes not found.");
  }

  private static PeResourceFile _InvokeFromRawImage(RawImage raw) {
    var map = typeof(PeResourceFile).GetInterfaceMap(typeof(IImageFileFormat<PeResourceFile>));
    foreach (var method in map.TargetMethods)
      if (method.Name.Contains("FromRawImage"))
        return (PeResourceFile)method.Invoke(null, new object[] { raw })!;
    throw new InvalidOperationException("FromRawImage not found.");
  }
}
