using System;
using System.IO;
using System.Text;
using FileFormat.Core;
using FileFormat.KodakDc25;

namespace FileFormat.KodakDc25.Tests;

[TestFixture]
public sealed class KodakDc25Tests {

  /// <summary>A file of the shape these have: a big-endian TIFF naming the camera, then the array.</summary>
  private static byte[] _File(int sensorWidth, string model = KodakDc25File.Model, int extra = 0) {
    var data = new byte[KodakDc25File.SensorOffset + sensorWidth * KodakDc25File.SensorHeight + extra];
    data[0] = (byte)'M';
    data[1] = (byte)'M';
    data[2] = 0x00;
    data[3] = 0x2A;
    Encoding.ASCII.GetBytes(model).CopyTo(data.AsSpan(386));

    // Something other than a flat field, so a decode that dropped the mosaic would show.
    for (var i = KodakDc25File.SensorOffset; i < data.Length; ++i)
      data[i] = (byte)(i * 11);

    return data;
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_Null_ThrowsArgumentNullException()
    => Assert.Throws<ArgumentNullException>(() => KodakDc25Reader.FromBytes(null!));

  [Test]
  [Category("Unit")]
  public void FromBytes_NotATiff_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => KodakDc25Reader.FromBytes(new byte[KodakDc25File.SensorOffset + 64]));

  [Test]
  [Category("Unit")]
  public void FromBytes_AnotherCamera_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => KodakDc25Reader.FromBytes(_File(KodakDc25File.WideSensorWidth, "KODAK DC40")));

  [Test]
  [Category("Unit")]
  public void FromBytes_ALengthThatIsNeitherArray_ThrowsInvalidDataException()
    => Assert.Throws<InvalidDataException>(() => KodakDc25Reader.FromBytes(_File(KodakDc25File.WideSensorWidth, extra: 1)));

  [Test]
  [Category("Unit")]
  public void FromBytes_TheWideArrayRendersToTheSizeTheCameraStates() {
    var decoded = KodakDc25File.ToRawImage(KodakDc25Reader.FromBytes(_File(KodakDc25File.WideSensorWidth)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(KodakDc25File.WideWidth));
      Assert.That(decoded.Height, Is.EqualTo(379), "the photosites are not square, so the short axis is stretched");
      Assert.That(decoded.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(decoded.PixelData, Has.Length.EqualTo(KodakDc25File.WideWidth * 379 * 3));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_TheNarrowArrayRendersToFourByThree() {
    var decoded = KodakDc25File.ToRawImage(KodakDc25Reader.FromBytes(_File(KodakDc25File.NarrowSensorWidth)));

    Assert.Multiple(() => {
      Assert.That(decoded.Width, Is.EqualTo(323));
      Assert.That(decoded.Height, Is.EqualTo(KodakDc25File.CroppedHeight));
      Assert.That(decoded.PixelData, Has.Length.EqualTo(323 * KodakDc25File.CroppedHeight * 3));
    });
  }

  [Test]
  [Category("Unit")]
  public void FromBytes_ThePictureIsNotTheEightyBySixtyThumbnail() {
    // The trap this format exists to avoid: the file's first TIFF directory is a reduced-resolution
    // copy, and anything answering with that is answering with a thumbnail.
    var decoded = KodakDc25File.ToRawImage(KodakDc25Reader.FromBytes(_File(KodakDc25File.WideSensorWidth)));

    Assert.That(decoded.Width * decoded.Height, Is.GreaterThan(80 * 60 * 10));
  }

  [Test]
  [Category("Unit")]
  public void MatchesSignature_TakesTheseAheadOfTheTiffReader() {
    Assert.Multiple(() => {
      Assert.That(FormatIO.MatchesSignature<KodakDc25File>(_File(KodakDc25File.WideSensorWidth)), Is.True);
      Assert.That(FormatIO.MatchesSignature<KodakDc25File>(_File(KodakDc25File.NarrowSensorWidth)), Is.True);
      Assert.That(FormatIO.MatchesSignature<KodakDc25File>(_File(KodakDc25File.WideSensorWidth, "KODAK DC40")), Is.Not.True);
      Assert.That(FormatIO.MatchesSignature<KodakDc25File>(_File(KodakDc25File.WideSensorWidth, extra: 1)), Is.Not.True);
    });
  }
}
