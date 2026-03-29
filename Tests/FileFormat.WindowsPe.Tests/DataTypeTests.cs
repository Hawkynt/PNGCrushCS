using System;
using FileFormat.Core;
using FileFormat.WindowsPe;

namespace FileFormat.WindowsPe.Tests;

[TestFixture]
public sealed class DataTypeTests {

  [Test]
  [Category("Unit")]
  public void PeImageResourceType_Icon_IsZero()
    => Assert.That((int)PeImageResourceType.Icon, Is.EqualTo(0));

  [Test]
  [Category("Unit")]
  public void PeImageResourceType_Cursor_IsOne()
    => Assert.That((int)PeImageResourceType.Cursor, Is.EqualTo(1));

  [Test]
  [Category("Unit")]
  public void PeImageResourceType_Bitmap_IsTwo()
    => Assert.That((int)PeImageResourceType.Bitmap, Is.EqualTo(2));

  [Test]
  [Category("Unit")]
  public void PeImageResourceType_EmbeddedImage_IsThree()
    => Assert.That((int)PeImageResourceType.EmbeddedImage, Is.EqualTo(3));

  [Test]
  [Category("Unit")]
  public void PeImageResourceType_HasFourValues()
    => Assert.That(Enum.GetValues<PeImageResourceType>(), Has.Length.EqualTo(4));

  [Test]
  [Category("Unit")]
  public void PeImageResource_Defaults_AreCorrect() {
    var resource = new PeImageResource();
    Assert.Multiple(() => {
      Assert.That(resource.ResourceType, Is.EqualTo(PeImageResourceType.Icon));
      Assert.That(resource.ResourceId, Is.EqualTo(0));
      Assert.That(resource.Data, Is.Empty);
      Assert.That(resource.FormatHint, Is.Null);
    });
  }

  [Test]
  [Category("Unit")]
  public void PeImageResource_InitProperties_RoundTrip() {
    var data = new byte[] { 1, 2, 3 };
    var resource = new PeImageResource {
      ResourceType = PeImageResourceType.Bitmap,
      ResourceId = 42,
      Data = data,
      FormatHint = "bmp",
    };
    Assert.Multiple(() => {
      Assert.That(resource.ResourceType, Is.EqualTo(PeImageResourceType.Bitmap));
      Assert.That(resource.ResourceId, Is.EqualTo(42));
      Assert.That(resource.Data, Is.SameAs(data));
      Assert.That(resource.FormatHint, Is.EqualTo("bmp"));
    });
  }

  [Test]
  [Category("Unit")]
  public void PeIconGroup_Defaults_AreCorrect() {
    var group = new PeIconGroup();
    Assert.Multiple(() => {
      Assert.That(group.GroupId, Is.EqualTo(0));
      Assert.That(group.IsCursor, Is.False);
      Assert.That(group.IcoData, Is.Empty);
    });
  }

  [Test]
  [Category("Unit")]
  public void PeIconGroup_InitProperties_RoundTrip() {
    var data = new byte[] { 0, 0, 1, 0, 1, 0 };
    var group = new PeIconGroup {
      GroupId = 5,
      IsCursor = true,
      IcoData = data,
    };
    Assert.Multiple(() => {
      Assert.That(group.GroupId, Is.EqualTo(5));
      Assert.That(group.IsCursor, Is.True);
      Assert.That(group.IcoData, Is.SameAs(data));
    });
  }

  [Test]
  [Category("Unit")]
  public void PeResourceFile_Defaults_AreCorrect() {
    var file = new PeResourceFile();
    Assert.Multiple(() => {
      Assert.That(file.IconGroups, Is.Empty);
      Assert.That(file.ImageResources, Is.Empty);
    });
  }

  [Test]
  [Category("Unit")]
  public void PrimaryExtension_IsExe() {
    var ext = _GetStaticProperty<string>("PrimaryExtension");
    Assert.That(ext, Is.EqualTo(".exe"));
  }

  [Test]
  [Category("Unit")]
  public void FileExtensions_ContainsAllFormats() {
    var exts = _GetStaticProperty<string[]>("FileExtensions");
    Assert.Multiple(() => {
      Assert.That(exts, Does.Contain(".exe"));
      Assert.That(exts, Does.Contain(".dll"));
      Assert.That(exts, Does.Contain(".ocx"));
      Assert.That(exts, Does.Contain(".scr"));
      Assert.That(exts, Does.Contain(".cpl"));
      Assert.That(exts, Has.Length.EqualTo(5));
    });
  }

  [Test]
  [Category("Unit")]
  public void Capabilities_HasMultiImage() {
    var caps = _GetStaticProperty<FormatCapability>("Capabilities");
    Assert.That(caps, Is.EqualTo(FormatCapability.MultiImage));
  }

  [Test]
  [Category("Unit")]
  public void FormatMagicBytes_IsMZ() {
    var attrs = typeof(PeResourceFile).GetCustomAttributes(typeof(FormatMagicBytesAttribute), false);
    Assert.That(attrs, Has.Length.EqualTo(1));
    var attr = (FormatMagicBytesAttribute)attrs[0];
    Assert.Multiple(() => {
      Assert.That(attr.Signature, Is.EqualTo(new byte[] { 0x4D, 0x5A }));
      Assert.That(attr.Offset, Is.EqualTo(0));
    });
  }

  [Test]
  [Category("Unit")]
  public void FormatDetectionPriority_Is999() {
    var attrs = typeof(PeResourceFile).GetCustomAttributes(typeof(FormatDetectionPriorityAttribute), false);
    Assert.That(attrs, Has.Length.EqualTo(1));
    var attr = (FormatDetectionPriorityAttribute)attrs[0];
    Assert.That(attr.Priority, Is.EqualTo(999));
  }

  private static T _GetStaticProperty<T>(string name) {
    var map = typeof(PeResourceFile).GetInterfaceMap(typeof(IImageFileFormat<PeResourceFile>));
    foreach (var method in map.TargetMethods)
      if (method.Name.Contains(name))
        return (T)method.Invoke(null, null)!;
    throw new InvalidOperationException("Property " + name + " not found.");
  }
}
