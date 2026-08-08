using System;
using System.Text;
using FileFormat.Core;

namespace FileFormat.PhotoParade.Tests;

[TestFixture]
public sealed class FromRawImageTests {

  private static RawImage _Ramp(int width, int height) {
    var data = new byte[width * height * 3];
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 3;
        data[at] = (byte)(100 + x % 128);
        data[at + 1] = (byte)(110 + y % 128);
        data[at + 2] = 128;
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Rgb24, PixelData = data };
  }

  private static PhotoParadeFile _RoundTrip(RawImage image)
    => PhotoParadeReader.FromBytes(PhotoParadeWriter.ToBytes(PhotoParadeFile.FromRawImage(image)));

  [Test]
  [Category("Integration")]
  public void RoundTrip_Ramp_ComesBackAtItsSizeAndVeryNearlyItsColours() {
    var source = _Ramp(37, 11);
    var decoded = PhotoParadeFile.ToRawImage(_RoundTrip(source));
    var rgb = PixelConverter.Convert(decoded, PixelFormat.Rgb24);

    long error = 0;
    for (var i = 0; i < source.PixelData.Length; ++i)
      error += Math.Abs(rgb.PixelData[i] - source.PixelData[i]);

    Assert.Multiple(() => {
      Assert.That((decoded.Width, decoded.Height), Is.EqualTo((37, 11)));
      Assert.That((double)error / source.PixelData.Length, Is.LessThan(4.0));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_KeepsWhateverSizeItIsGiven() {
    var wide = PhotoParadeFile.ToRawImage(_RoundTrip(_Ramp(200, 3)));
    var tall = PhotoParadeFile.ToRawImage(_RoundTrip(_Ramp(3, 200)));

    Assert.Multiple(() => {
      Assert.That((wide.Width, wide.Height), Is.EqualTo((200, 3)));
      Assert.That((tall.Width, tall.Height), Is.EqualTo((3, 200)));
    });
  }

  [Test]
  [Category("Integration")]
  public void FromRawImage_AcceptsAFormatOtherThanItsOwn() {
    var grey = new RawImage { Width = 37, Height = 11, Format = PixelFormat.Gray8, PixelData = new byte[37 * 11] };

    Assert.That(PhotoParadeFile.ToRawImage(_RoundTrip(grey)).Width, Is.EqualTo(37));
  }

  /// <summary>
  /// A description block stands immediately after the photograph it describes, and that is how the
  /// photograph is found: it is the run whose own markers end exactly where the block begins. Writing
  /// the block first, or leaving anything between the two, produces a file in which no photograph can
  /// be located at all.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_PutsTheDescriptionBlockAfterItsPhotograph() {
    var file = PhotoParadeFile.FromRawImage(_Ramp(37, 11));
    var bytes = PhotoParadeWriter.ToBytes(file);
    var jpeg = file.Photographs[0].Embedded;

    Assert.Multiple(() => {
      Assert.That(bytes.AsSpan(PhotoParadeFile.MagicOffset, 4).SequenceEqual(PhotoParadeFile.Magic), Is.True);
      Assert.That(bytes.AsSpan(PhotoParadeFile.SubFormatOffset, 4).SequenceEqual(PhotoParadeFile.SubFormat), Is.True);
      Assert.That(bytes.AsSpan(PhotoParadeFile.HeaderSize, jpeg.Length).ToArray(), Is.EqualTo(jpeg), "the photograph comes first");
      Assert.That(
        bytes.AsSpan(PhotoParadeFile.HeaderSize + jpeg.Length, 4).SequenceEqual(PhotoParadeFile.PictureInfoTag),
        Is.True, "and the block describing it begins where it ends");
    });
  }

  /// <summary>
  /// The album block states how many photographs there are, and the reader refuses a file where that
  /// count and the number it found disagree — a partial album drawn as a whole one being exactly the
  /// quiet wrongness the check is for.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void ToBytes_StatesTheCountTheWalkWillFind() {
    var bytes = PhotoParadeWriter.ToBytes(PhotoParadeFile.FromRawImage(_Ramp(37, 11)));
    var text = Encoding.Latin1.GetString(bytes);

    var album = text.LastIndexOf("LBUM", StringComparison.Ordinal);
    var count = text.IndexOf("NUMP", album, StringComparison.Ordinal);

    Assert.Multiple(() => {
      Assert.That(album, Is.GreaterThan(0));
      Assert.That(count, Is.GreaterThan(album));
      Assert.That(bytes[count + 11], Is.EqualTo(1), "one photograph");
      Assert.That(PhotoParadeFile.ImageCount(PhotoParadeReader.FromBytes(bytes)), Is.EqualTo(1));
    });
  }
}
