using System;
using System.Collections.Generic;
using System.IO;
using FileFormat.Cgm;
using FileFormat.Core;

namespace FileFormat.Cgm.Tests;

[TestFixture]
public sealed class CgmTests {

  private const int _ClassDelimiter = 0, _ClassPictureDescriptor = 2, _ClassPrimitive = 4, _ClassAttribute = 5;

  /// <summary>One command, header word and parameters, padded to a word as the encoding pads.</summary>
  private static byte[] _Command(int elementClass, int elementId, params byte[] parameters) {
    var bytes = new List<byte>();
    var length = parameters.Length < 31 ? parameters.Length : 31;
    var header = (elementClass << 12) | (elementId << 5) | length;
    bytes.Add((byte)(header >> 8));
    bytes.Add((byte)header);

    if (length == 31) {
      bytes.Add((byte)(parameters.Length >> 8));
      bytes.Add((byte)parameters.Length);
    }

    bytes.AddRange(parameters);
    if (parameters.Length % 2 == 1)
      bytes.Add(0);

    return bytes.ToArray();
  }

  private static byte[] _Word(int value) => [(byte)(value >> 8), (byte)value];

  private static byte[] _Words(params int[] values) {
    var bytes = new List<byte>();
    foreach (var value in values)
      bytes.AddRange(_Word(value));

    return bytes.ToArray();
  }

  private static byte[] _Metafile(params byte[][] commands) {
    var bytes = new List<byte>();
    bytes.AddRange(_Command(_ClassDelimiter, 1, 4, (byte)'t', (byte)'e', (byte)'s', (byte)'t'));
    bytes.AddRange(_Command(_ClassDelimiter, 3, 0));
    bytes.AddRange(_Command(_ClassPictureDescriptor, 6, _Words(0, 0, 1000, 500)));
    bytes.AddRange(_Command(_ClassDelimiter, 4));

    foreach (var command in commands)
      bytes.AddRange(command);

    bytes.AddRange(_Command(_ClassDelimiter, 5));
    bytes.AddRange(_Command(_ClassDelimiter, 2));
    return bytes.ToArray();
  }

  /// <summary>An INTERIOR STYLE command, which the standard states as an enumeration.</summary>
  private static byte[] _InteriorStyle(int style) => _Command(_ClassAttribute, 22, _Word(style));

  /// <summary>A FILL COLOUR command, which in indexed mode is one index at the colour index precision.</summary>
  private static byte[] _FillColour(byte index) => _Command(_ClassAttribute, 23, index);

  /// <summary>A COLOUR TABLE command: a starting index and then colours three components each.</summary>
  private static byte[] _ColourTable(byte from, params byte[] components) {
    var bytes = new List<byte> { from };
    bytes.AddRange(components);
    return _Command(_ClassAttribute, 34, bytes.ToArray());
  }

  private static byte[] _Polygon(params int[] coordinates) => _Command(_ClassPrimitive, 7, _Words(coordinates));

  private static double _Coverage(RawImage image) {
    var pixels = image.PixelData;
    var total = 0.0;
    for (var i = 0; i < pixels.Length; i += 4)
      total += (255 - pixels[i]) / 255.0;

    return total / (image.Width * image.Height);
  }

  private static byte _At(RawImage image, int x, int y) => image.PixelData[(y * image.Width + x) * 4];

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => CgmReader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_DoesNotOpenWithBeginMetafile_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => CgmReader.FromBytes(new byte[64]));

  [Test]
  [Category("Unit")]
  public void FromBytes_ClearTextEncoding_IsRefused()
    => Assert.Throws<InvalidDataException>(() => CgmReader.FromBytes("BegMF \"test\";"u8.ToArray()));

  /// <summary>
  /// Arriving at END METAFILE is what says the lengths were read as lengths, so a file that never
  /// gets there has not been read at all.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void FromBytes_WithoutEndMetafile_ThrowsInvalidDataException() {
    var data = _Metafile();
    Array.Resize(ref data, data.Length - 2);

    Assert.Throws<InvalidDataException>(() => CgmReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ACommandLongerThanTheFile_ThrowsInvalidDataException() {
    var data = _Metafile();
    // Turn the first command's length into one the file cannot hold.
    data[1] = (byte)((data[1] & 0xE0) | 30);

    Assert.Throws<InvalidDataException>(() => CgmReader.FromBytes(data));
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ReadsEveryCommandAndTheMetafilesOwnName() {
    var file = CgmReader.FromBytes(_Metafile(_InteriorStyle(CgmState.InteriorSolid), _Polygon(0, 0, 1000, 0, 1000, 500, 0, 500)));

    Assert.Multiple(() => {
      Assert.That(file.Name, Is.EqualTo("test"));
      Assert.That(file.Commands, Has.Count.EqualTo(8));
    });
  }

  /// <summary>The picture is drawn at the extent the file states for it, and at its shape.</summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_TakesItsShapeFromTheStatedExtent() {
    var image = CgmFile.ToRawImage(CgmReader.FromBytes(_Metafile(_Polygon(0, 0, 1000, 0, 1000, 500, 0, 500))));

    Assert.That((double)image.Width / image.Height, Is.EqualTo(2).Within(0.01), "an extent of a thousand by five hundred");
  }

  [Test]
  [Category("Unit")]
  public void ToRawImage_ASolidPolygonCoversTheExtentAndAHollowOneDoesNot() {
    var solid = CgmFile.ToRawImage(CgmReader.FromBytes(_Metafile(
      _ColourTable(1, 0, 0, 0),
      _InteriorStyle(CgmState.InteriorSolid),
      _FillColour(1),
      _Polygon(0, 0, 1000, 0, 1000, 500, 0, 500))));

    var hollow = CgmFile.ToRawImage(CgmReader.FromBytes(_Metafile(
      _InteriorStyle(CgmState.InteriorHollow),
      _Polygon(0, 0, 1000, 0, 1000, 500, 0, 500))));

    Assert.Multiple(() => {
      Assert.That(_Coverage(solid), Is.GreaterThan(0.95));
      Assert.That(_Coverage(hollow), Is.LessThan(0.02));
    });
  }

  /// <summary>
  /// The picture's y axis points up and a raster's first row is its top, so a shape in the lower
  /// half of the picture has to come out in the lower half of the image.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_TurnsThePictureOverForTheRaster() {
    var image = CgmFile.ToRawImage(CgmReader.FromBytes(_Metafile(
      _ColourTable(1, 0, 0, 0),
      _InteriorStyle(CgmState.InteriorSolid),
      _FillColour(1),
      _Polygon(0, 0, 1000, 0, 1000, 250, 0, 250))));

    Assert.Multiple(() => {
      Assert.That(_At(image, image.Width / 2, image.Height * 3 / 4), Is.Zero, "the bottom of the picture is the bottom of the image");
      Assert.That(_At(image, image.Width / 2, image.Height / 4), Is.EqualTo(255));
    });
  }

  /// <summary>
  /// The colour table is what an index means. A reader that ignored it would paint everything the
  /// same colour and this would not notice; one that read it at the wrong width would paint the
  /// wrong one.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_AnIndexIsLookedUpInTheColourTable() {
    var image = CgmFile.ToRawImage(CgmReader.FromBytes(_Metafile(
      _ColourTable(1, 255, 0, 0, 0, 0, 255),
      _InteriorStyle(CgmState.InteriorSolid),
      _FillColour(2),
      _Polygon(0, 0, 1000, 0, 1000, 500, 0, 500))));

    var at = (image.Height / 2 * image.Width + image.Width / 2) * 4;
    Assert.Multiple(() => {
      Assert.That(image.PixelData[at], Is.Zero, "index two was defined as blue");
      Assert.That(image.PixelData[at + 2], Is.EqualTo(255));
    });
  }

  /// <summary>
  /// The polygon set carries a flag after every point saying whether the contour closes there. Two
  /// squares in one command have to come out as two squares rather than as one figure of eight.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToRawImage_APolygonSetClosesWhereItsFlagsSay() {
    var parameters = new List<byte>();
    void Vertex(int x, int y, int flag) {
      parameters.AddRange(_Words(x, y, flag));
    }

    Vertex(0, 0, 1);
    Vertex(400, 0, 1);
    Vertex(400, 500, 1);
    Vertex(0, 500, 3);
    Vertex(600, 0, 1);
    Vertex(1000, 0, 1);
    Vertex(1000, 500, 1);
    Vertex(600, 500, 3);

    var image = CgmFile.ToRawImage(CgmReader.FromBytes(_Metafile(
      _ColourTable(1, 0, 0, 0),
      _InteriorStyle(CgmState.InteriorSolid),
      _FillColour(1),
      _Command(_ClassPrimitive, 8, parameters.ToArray()))));

    Assert.Multiple(() => {
      Assert.That(_At(image, image.Width / 5, image.Height / 2), Is.Zero, "the left square is filled");
      Assert.That(_At(image, image.Width * 4 / 5, image.Height / 2), Is.Zero, "so is the right one");
      Assert.That(_At(image, image.Width / 2, image.Height / 2), Is.EqualTo(255), "and the gap between them is not");
    });
  }

  [Test]
  [Category("Unit")]
  public void MatchesSignature_TakesBeginMetafileAndNothingElse() {
    Assert.Multiple(() => {
      Assert.That(_Matches([0x00, 0x3F]), Is.True, "class zero, element one, a long parameter list");
      Assert.That(_Matches([0x00, 0x26]), Is.True);
      Assert.That(_Matches([0x40, 0x20]), Is.False, "a different class");
      Assert.That(_Matches([0x00]), Is.Null);
    });
  }

  private static bool? _Matches(byte[] header) => _Signature<CgmFile>(header);

  /// <summary>Asks a format its own opinion of a header, which only a type parameter can.</summary>
  private static bool? _Signature<T>(byte[] header) where T : IImageFormatMetadata<T> => T.MatchesSignature(header);
}
