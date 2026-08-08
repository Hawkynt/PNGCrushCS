using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.Gif;
using FileFormat.Pcx;
using FileFormat.Psd;
using FileFormat.Vue;

namespace FileFormat.Vue.Tests;

/// <summary>
/// A Vue d'Esprit object, and two names that turned out to belong to formats already here.
/// </summary>
/// <remarks>
/// The Vue file is assembled here the way the format lays one out — the program's name, two strings
/// each preceded by its length, the size of the picture, and then the picture — so that what is
/// checked is arriving at the picture by following the fields rather than by searching for its
/// signature.
/// </remarks>
[TestFixture]
public sealed class VueTests {

  private static byte[] _Gif(int width, int height) {
    var image = new RawImage {
      Width = width,
      Height = height,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[width * height * 3],
    };

    for (var i = 0; i < width * height; ++i)
      image.PixelData[i * 3] = (byte)(i * 3);

    return GifWriter.ToBytes(GifFile.FromRawImage(image));
  }

  private static byte[] _Build(string description, string name, int statedWidth, int statedHeight, byte[] picture) {
    using var file = new MemoryStream();
    file.Write("Vue d'Esprit\0"u8);
    file.Write(" Version 2.0  vob"u8);

    var field = new byte[4];
    void String(string text) {
      var bytes = Encoding.Latin1.GetBytes(text);
      BinaryPrimitives.WriteUInt16LittleEndian(field, (ushort)bytes.Length);
      file.Write(field, 0, 2);
      file.Write(bytes);
    }

    String(description);
    String(name);
    BinaryPrimitives.WriteUInt32LittleEndian(field, (uint)statedWidth);
    file.Write(field);
    BinaryPrimitives.WriteUInt32LittleEndian(field, (uint)statedHeight);
    file.Write(field);
    file.Write(picture);
    return file.ToArray();
  }

  [Test]
  [Category("Unit")]
  public void TheFieldsLeadToThePicture() {
    var file = VueReader.FromBytes(_Build("Final result of the tutorial", "Simple house", 12, 7, _Gif(12, 7)));
    var image = VueFile.ToRawImage(file);

    Assert.Multiple(() => {
      Assert.That(file.Name, Is.EqualTo("Simple house"));
      Assert.That(file.Description, Is.EqualTo("Final result of the tutorial"));
      Assert.That(file.Width, Is.EqualTo(12));
      Assert.That(file.Height, Is.EqualTo(7));
      Assert.That(image.Width, Is.EqualTo(12));
      Assert.That(image.Height, Is.EqualTo(7));
    });
  }

  [Test]
  [Category("Unit")]
  public void ASizeThePictureDisagreesWithIsRefused() {
    Assert.That(() => VueReader.FromBytes(_Build("d", "n", 13, 7, _Gif(12, 7))), Throws.InstanceOf<InvalidDataException>());
  }

  [Test]
  [Category("Unit")]
  public void AStringLengthThatMissesThePictureIsRefused() {
    var bytes = _Build("description", "name", 12, 7, _Gif(12, 7));

    // Lengthen the first string by one, so following the fields lands beside the picture.
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(30), (ushort)("description".Length + 1));
    Assert.That(() => VueReader.FromBytes(bytes), Throws.InstanceOf<InvalidDataException>());
  }

  [Test]
  [Category("Unit")]
  public void SomethingElseEntirelyIsRefused() {
    Assert.That(() => VueReader.FromBytes(Encoding.ASCII.GetBytes("not a Vue object at all, no")), Throws.InstanceOf<InvalidDataException>());
  }

  private static string[] _Extensions<T>() where T : IImageFormatMetadata<T> => T.FileExtensions;

  [Test]
  [Category("Unit")]
  public void TheBibleLibrariesAreClaimedByPcx() {
    Assert.That(_Extensions<PcxFile>(), Does.Contain(".bmg").And.Contain(".ibg"));
  }

  [Test]
  [Category("Unit")]
  public void PhotoDeluxeIsClaimedByPhotoshop() {
    Assert.That(_Extensions<PsdFile>(), Does.Contain(".pdd"));
  }
}
