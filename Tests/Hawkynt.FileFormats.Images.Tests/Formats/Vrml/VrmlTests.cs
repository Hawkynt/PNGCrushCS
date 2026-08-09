using System;
using System.IO;
using System.Text;
using FileFormat.Core;

namespace FileFormat.Vrml.Tests;

/// <summary>
/// VRML 2.0: a scene, and inside it a PixelTexture that really is a picture.
/// </summary>
/// <remarks>
/// The scene below is not written by hand. It is what XnView's own converter emits for a six by four
/// picture whose red runs with x, whose green runs with y and whose blue is the product of the two —
/// deliberately asymmetric both ways round, so that a mirrored or transposed reading cannot pass.
/// Its converter will not read the file back, so what is checked is that the pixels come out equal to
/// the ones that went into it.
/// </remarks>
[TestFixture]
public sealed class VrmlTests {

  /// <summary>The picture the scene below carries: red with x, green with y, blue their product.</summary>
  private static byte[] _Source() {
    var pixels = new byte[6 * 4 * 3];
    for (var y = 0; y < 4; ++y)
    for (var x = 0; x < 6; ++x) {
      var at = (y * 6 + x) * 3;
      pixels[at] = (byte)(x * 40);
      pixels[at + 1] = (byte)(y * 60);
      pixels[at + 2] = (byte)(x * y * 11);
    }

    return pixels;
  }

  /// <summary>Exactly what the converter writes, line breaks and all.</summary>
  private const string _Scene = """
    #VRML V2.0 utf8
    Group {
      children [
        Shape {
          appearance Appearance {
            material Material {
              diffuseColor 1.0 1.0 1.0
            }
            texture PixelTexture {
              image 6 4 3
    0x00b400 0x28b421 0x50b442 0x78b463
    0xa0b484 0xc8b4a5 0x007800 0x287816 0x50782c 0x787842
    0xa07858 0xc8786e 0x003c00 0x283c0b 0x503c16 0x783c21
    0xa03c2c 0xc83c37 0x000000 0x280000 0x500000 0x780000
    0xa00000 0xc80000         }
          }
          geometry Box {}
        }
      ]
    }
    """;

  private static byte[] _Bytes(string text) => Encoding.ASCII.GetBytes(text);

  [Test]
  [Category("Integration")]
  public void Read_ReturnsThePixelsTheConverterWasGiven() {
    var image = VrmlFile.ToRawImage(VrmlReader.FromBytes(_Bytes(_Scene)));
    var expected = _Source();

    Assert.Multiple(() => {
      Assert.That(image.Width, Is.EqualTo(6));
      Assert.That(image.Height, Is.EqualTo(4));
      Assert.That(image.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(image.PixelData, Is.EqualTo(expected));
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_TakesTheFirstRowOfTheFieldAsTheBottomRowOfThePicture() {
    // A texture's origin is its lower-left corner. Read the other way up this comes back mirrored.
    var image = VrmlFile.ToRawImage(VrmlReader.FromBytes(_Bytes(
      "#VRML V2.0 utf8\nPixelTexture { image 1 2 1 0x11 0x22 }")));

    Assert.Multiple(() => {
      Assert.That(image.PixelData[0], Is.EqualTo(0x22), "the top row");
      Assert.That(image.PixelData[1], Is.EqualTo(0x11), "the bottom row");
    });
  }

  [TestCase(1, PixelFormat.Gray8)]
  [TestCase(2, PixelFormat.GrayAlpha16)]
  [TestCase(3, PixelFormat.Rgb24)]
  [TestCase(4, PixelFormat.Rgba32)]
  [Category("Unit")]
  public void Read_TakesTheComponentCountTheFieldStates(int components, PixelFormat format) {
    var value = "0x" + new string('1', components * 2);
    var image = VrmlFile.ToRawImage(VrmlReader.FromBytes(_Bytes(
      $"#VRML V2.0 utf8\nPixelTexture {{ image 1 1 {components} {value} }}")));

    Assert.Multiple(() => {
      Assert.That(image.Format, Is.EqualTo(format));
      Assert.That(image.PixelData, Has.Length.EqualTo(components));
      Assert.That(image.PixelData[0], Is.EqualTo(0x11));
    });
  }

  [Test]
  [Category("Unit")]
  public void Read_TakesThePixelsInDecimalToo() {
    // The language allows either, and only one producer's habit is checked above.
    var image = VrmlFile.ToRawImage(VrmlReader.FromBytes(_Bytes(
      "#VRML V2.0 utf8\nPixelTexture { image 2 1 1 17, 34 }")));

    Assert.That(image.PixelData, Is.EqualTo(new byte[] { 17, 34 }));
  }

  [Test]
  [Category("Unit")]
  public void Read_IgnoresAPixelTextureNamedInAComment() {
    // A comment runs to the end of its line, and the header is itself one.
    Assert.Throws<InvalidDataException>(() => VrmlReader.FromBytes(_Bytes(
      "#VRML V2.0 utf8\n# PixelTexture { image 1 1 1 0x00 }\nGroup { }")));
  }

  [Test]
  [Category("Unit")]
  public void Read_RefusesAFileThatIsNotAScene()
    => Assert.Throws<InvalidDataException>(() => VrmlReader.FromBytes(_Bytes("not a scene at all")));

  [Test]
  [Category("Unit")]
  public void Read_RefusesASceneWithNoPictureInIt()
    => Assert.Throws<InvalidDataException>(() => VrmlReader.FromBytes(_Bytes(
      "#VRML V2.0 utf8\nShape { geometry Box {} }")));

  [Test]
  [Category("Unit")]
  public void Read_RefusesAFieldShortOfThePixelsItStates()
    => Assert.Throws<InvalidDataException>(() => VrmlReader.FromBytes(_Bytes(
      "#VRML V2.0 utf8\nPixelTexture { image 2 2 1 0x00 0x01 0x02 }")));

  [Test]
  [Category("Unit")]
  public void Read_RefusesAFieldCarryingMorePixelsThanItStates()
    => Assert.Throws<InvalidDataException>(() => VrmlReader.FromBytes(_Bytes(
      "#VRML V2.0 utf8\nPixelTexture { image 2 1 1 0x00 0x01 0x02 }")));

  [Test]
  [Category("Unit")]
  public void Read_RefusesAComponentCountNoPixelHas()
    => Assert.Throws<InvalidDataException>(() => VrmlReader.FromBytes(_Bytes(
      "#VRML V2.0 utf8\nPixelTexture { image 1 1 5 0x00 }")));

  private static RawImage _Rgb(int width, int height, byte[] pixels)
    => new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = pixels };

  /// <summary>
  /// The whole file for a four-pixel picture, written out so the shape of the output is readable.
  /// </summary>
  /// <remarks>
  /// Every space matters and none is decoration: a value is followed by a space whether or not a line
  /// break comes after it, and the brace that closes the texture is indented eight. Byte equality with
  /// the converter's own output was checked separately at 6x4, 5x3, 10x2, 13x2, 7x1, 1x7 and 4x2; this
  /// is the same agreement written where it can be read.
  /// </remarks>
  private const string _FourPixels =
    "#VRML V2.0 utf8\n"
    + "Group {\n"
    + "  children [\n"
    + "    Shape {\n"
    + "      appearance Appearance {\n"
    + "        material Material {\n"
    + "          diffuseColor 1.0 1.0 1.0\n"
    + "        }\n"
    + "        texture PixelTexture {\n"
    + "          image 4 1 1\n"
    + "0x11 0x22 0x33 0x44 \n"
    + "        }\n"
    + "      }\n"
    + "      geometry Box {}\n"
    + "    }\n"
    + "  ]\n"
    + "}\n";

  [Test]
  [Category("Unit")]
  public void Write_ProducesTheSceneTheConverterWritesDownToTheSpacing() {
    var grey = new RawImage {
      Width = 4, Height = 1, Format = PixelFormat.Gray8, PixelData = [0x11, 0x22, 0x33, 0x44],
    };

    Assert.That(Encoding.ASCII.GetString(FormatIO.Encode<VrmlFile>(grey)), Is.EqualTo(_FourPixels));
  }

  /// <summary>
  /// What we write parses to the same picture as the scene the converter wrote for it.
  /// </summary>
  /// <remarks>
  /// The two files are not byte-identical here only because the fixture above has had its trailing
  /// spaces trimmed by the source file; what is compared is therefore the picture rather than the text.
  /// </remarks>
  [Test]
  [Category("Integration")]
  public void Write_CarriesThePictureTheConvertersOwnSceneCarries() {
    var ours = VrmlFile.ToRawImage(VrmlReader.FromBytes(FormatIO.Encode<VrmlFile>(_Rgb(6, 4, _Source()))));
    var theirs = VrmlFile.ToRawImage(VrmlReader.FromBytes(_Bytes(_Scene)));

    Assert.Multiple(() => {
      Assert.That((ours.Width, ours.Height), Is.EqualTo((theirs.Width, theirs.Height)));
      Assert.That(ours.Format, Is.EqualTo(theirs.Format));
      Assert.That(ours.PixelData, Is.EqualTo(theirs.PixelData));
    });
  }

  [Test]
  [Category("Unit")]
  public void Write_ListsTheBottomRowFirst() {
    // Read back the other way up this picture would come out mirrored, and the round-trip below
    // would still pass — so the field itself is what is looked at.
    var text = Encoding.ASCII.GetString(FormatIO.Encode<VrmlFile>(new RawImage {
      Width = 1, Height = 2, Format = PixelFormat.Gray8, PixelData = [0x11, 0x22],
    }));

    // One pixel across never reaches a line break, so the two values stand on the one line.
    Assert.That(text, Does.Contain("image 1 2 1\n0x22 0x11         }"));
  }

  [Test]
  [Category("Unit")]
  public void Write_BreaksTheLineEveryFourPixelsOfARowRatherThanOfTheField() {
    // Six across: four on the first line, two on the second, and the next row starts its own count.
    var text = Encoding.ASCII.GetString(FormatIO.Encode<VrmlFile>(new RawImage {
      Width = 6, Height = 2, Format = PixelFormat.Gray8, PixelData = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12],
    }));

    // The bottom row is written first, so its four break the line and its last two carry the top
    // row's first four along with them — which is the wrapping the converter produces.
    Assert.That(text, Does.Contain(
      "0x07 0x08 0x09 0x0a \n0x0b 0x0c 0x01 0x02 0x03 0x04 \n0x05 0x06         }"));
  }

  [TestCase(PixelFormat.Gray8, 1)]
  [TestCase(PixelFormat.Rgb24, 3)]
  [TestCase(PixelFormat.Rgba32, 4)]
  [Category("Unit")]
  public void Write_SpendsTwoDigitsOnEveryComponentItStates(PixelFormat format, int components) {
    var text = Encoding.ASCII.GetString(FormatIO.Encode<VrmlFile>(new RawImage {
      Width = 1, Height = 1, Format = format, PixelData = new byte[components],
    }));

    Assert.That(text, Does.Contain($"image 1 1 {components}\n0x{new string('0', components * 2)} "));
  }

  /// <summary>
  /// Alpha survives a picture that keeps it blue-first, which choosing by layout alone would drop.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void Write_KeepsTheAlphaOfAPictureStoredBlueFirst() {
    var text = Encoding.ASCII.GetString(FormatIO.Encode<VrmlFile>(new RawImage {
      Width = 1, Height = 1, Format = PixelFormat.Bgra32, PixelData = [0x33, 0x22, 0x11, 0x44],
    }));

    Assert.That(text, Does.Contain("image 1 1 4\n0x11223344 "));
  }

  [Test]
  [Category("Unit")]
  public void Write_RoundTripsThePictureItWasGiven() {
    var source = _Rgb(6, 4, _Source());
    var back = FormatIO.Decode<VrmlFile>(FormatIO.Encode<VrmlFile>(source));

    Assert.Multiple(() => {
      Assert.That((back.Width, back.Height), Is.EqualTo((6, 4)));
      Assert.That(back.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(back.PixelData, Is.EqualTo(source.PixelData));
    });
  }
}
