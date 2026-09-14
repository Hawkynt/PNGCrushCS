using System;
using System.IO;
using FileFormat.Codecs.H263;
using FileFormat.Core;

namespace FileFormat.Codecs.H261.Tests;

/// <summary>ITU-T H.261 Annex D high-resolution still-image transmission.</summary>
/// <remarks>
/// These cases are specification-shaped rather than borrowed from ffmpeg: ffmpeg accepts the HI_RES
/// bit as ordinary H.261 but does not assemble Annex D's four sub-images. Figure D.1 and Annex D.3 are
/// therefore the oracle for the sample parity and picture-layer signalling exercised here.
/// </remarks>
[TestFixture]
public sealed class H261AnnexDTests {

  [Test]
  [Category("Unit")]
  public void FourQcifSubImagesAreInterleavedIntoOneCifStillImage() {
    int[] directCurrent = [32, 64, 96, 160];
    var decoder = H261VideoDecoder.Create(_Stream(176, 144));

    for (var index = 0; index < 3; ++index)
      Assert.That(
        decoder.TryDecode(new(0, _FlatStillSubImage(index, directCurrent[index])), out _),
        Is.False,
        $"sub-image {index} is not a complete high-resolution picture");

    Assert.That(decoder.TryDecode(new(0, _FlatStillSubImage(3, directCurrent[3])), out var frame), Is.True);

    Assert.Multiple(() => {
      Assert.That(frame.Width, Is.EqualTo(352));
      Assert.That(frame.Height, Is.EqualTo(288));
      Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));

      // Figure D.1:
      //   0 3 0 3 ...
      //   1 2 1 2 ...
      Assert.That(_Red(frame, 0, 0), Is.EqualTo(_Grey(directCurrent[0])));
      Assert.That(_Red(frame, 0, 1), Is.EqualTo(_Grey(directCurrent[1])));
      Assert.That(_Red(frame, 1, 1), Is.EqualTo(_Grey(directCurrent[2])));
      Assert.That(_Red(frame, 1, 0), Is.EqualTo(_Grey(directCurrent[3])));
    });
  }

  [Test]
  [Category("Unit")]
  public void AnnexDRejectsTemporalReferencesWhoseHighThreeBitsAreNotZero() {
    var stream = new H261TestStream()
      .PictureHeader(temporalReference: 4, requestStillImage: true)
      .ToArray();
    var decoder = H261VideoDecoder.Create(_Stream(176, 144));

    var failure = Assert.Throws<InvalidDataException>(() => decoder.TryDecode(new(0, stream), out _));

    Assert.Multiple(() => {
      Assert.That(failure!.Message, Does.Contain("Annex D.3"));
      Assert.That(failure.Message, Does.Contain("high bits"));
    });
  }

  [Test]
  [Category("Unit")]
  public void MotionVideoAbandonsAnIncompleteStillImageAssembly() {
    var decoder = H261VideoDecoder.Create(_Stream(176, 144));

    Assert.That(decoder.TryDecode(new(0, _FlatStillSubImage(0, 32)), out _), Is.False);
    Assert.That(decoder.TryDecode(new(0, _FlatStillSubImage(1, 64)), out _), Is.False);

    Assert.That(decoder.TryDecode(new(0, H261TestStream.FlatQcifIntraPicture(255)), out var motion), Is.True);
    Assert.Multiple(() => {
      Assert.That(motion.Width, Is.EqualTo(176));
      Assert.That(motion.Height, Is.EqualTo(144));
    });

    // If sub-images 0 and 1 survived the motion picture, these two would incorrectly complete an old
    // still image. Annex D.3 explicitly permits resuming motion video at any time.
    Assert.That(decoder.TryDecode(new(0, _FlatStillSubImage(2, 96)), out _), Is.False);
    Assert.That(decoder.TryDecode(new(0, _FlatStillSubImage(3, 160)), out _), Is.False);
  }

  [Test]
  [Category("Unit")]
  public void TheEncoderWritesFourSequentialUnpaddedAnnexDSubPictures() {
    var encoder = H261VideoEncoder.Create(_Stream(176, 144));
    Assert.That(encoder.TryEncodeStillImage(_StillPicture(352, 288), 0, out var packet), Is.True);
    Assert.That(packet.IsKeyFrame, Is.True);

    var reader = new H263BitReader(packet.Data.Span);
    H263Frame? reference = null;

    for (var index = 0; index < 4; ++index) {
      Assert.That(
        reader.ReadBits(H261PictureHeader.StartCodeLength),
        Is.EqualTo(H261PictureHeader.StartCode),
        $"sub-image {index} begins immediately with a picture start code");

      var header = H261PictureHeader.Parse(ref reader);
      Assert.Multiple(() => {
        Assert.That(header.IsStillImage, Is.True, $"sub-image {index} sets HI_RES to zero");
        Assert.That(header.TemporalReference, Is.EqualTo(index), $"sub-image {index} is identified by TR");
        Assert.That(header.Width, Is.EqualTo(176));
        Assert.That(header.Height, Is.EqualTo(144));
      });

      var picture = H261PictureDecoder.BeginPicture(header, reference);
      picture.DecodePicture(ref reader);
      reference = picture.Target;
    }

    Assert.That(reader.BitsRemaining, Is.LessThanOrEqualTo(7), "only final byte padding may remain");
    if (reader.BitsRemaining != 0)
      Assert.That(reader.NextBits(reader.BitsRemaining), Is.Zero);
  }

  [Test]
  [Category("Unit")]
  public void TheEncoderAndDecoderRoundTripFigureD1SampleParity() {
    byte[] luminance = [40, 80, 120, 160];
    var encoder = H261VideoEncoder.Create(_Stream(176, 144));
    var decoder = H261VideoDecoder.Create(encoder.DescribeStream());

    Assert.That(encoder.TryEncodeStillImage(_StillPicture(352, 288, luminance), 0, out var packet), Is.True);
    Assert.That(decoder.TryDecode(packet, out var frame), Is.True);

    Assert.Multiple(() => {
      Assert.That(frame.Width, Is.EqualTo(352));
      Assert.That(frame.Height, Is.EqualTo(288));
      Assert.That(_Red(frame, 0, 0), Is.EqualTo(_Grey(luminance[0])).Within(2));
      Assert.That(_Red(frame, 0, 1), Is.EqualTo(_Grey(luminance[1])).Within(2));
      Assert.That(_Red(frame, 1, 1), Is.EqualTo(_Grey(luminance[2])).Within(2));
      Assert.That(_Red(frame, 1, 0), Is.EqualTo(_Grey(luminance[3])).Within(2));
    });
  }

  [Test]
  [Category("Unit")]
  public void AnnexDStillImageEncodingRequiresExactlyDoubleTheStreamDimensions() {
    var encoder = H261VideoEncoder.Create(_Stream(176, 144));

    var failure = Assert.Throws<InvalidDataException>(
      () => encoder.TryEncodeStillImage(_StillPicture(176, 144), 0, out _));

    Assert.Multiple(() => {
      Assert.That(failure!.Message, Does.Contain("352x288"));
      Assert.That(failure.Message, Does.Contain("176x144"));
    });
  }

  private static byte[] _FlatStillSubImage(int index, int directCurrent) => new H261TestStream()
    .PictureHeader(temporalReference: index, requestStillImage: true)
    .FlatIntraGroup(1, 1, directCurrent)
    .FlatIntraGroup(3, 1, directCurrent)
    .FlatIntraGroup(5, 1, directCurrent)
    .ToArray();

  private static MediaStreamInfo _Stream(int width, int height) => new() {
    Index = 0,
    Kind = MediaStreamKind.Video,
    Codec = CodecTag.FromCharacters("H261"),
    Width = width,
    Height = height,
  };

  private static RawImage _StillPicture(int width, int height, byte[]? luminanceBySubImage = null) {
    luminanceBySubImage ??= [40, 80, 120, 160];
    var lumaSamples = width * height;
    var chromaSamples = lumaSamples / 4;
    var planes = new byte[lumaSamples + 2 * chromaSamples];

    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x)
        planes[y * width + x] = luminanceBySubImage[_SubImageAt(x, y)];

    planes.AsSpan(lumaSamples).Fill(128);
    return new() { Width = width, Height = height, Format = PixelFormat.Yuv420P8, PixelData = planes };
  }

  private static int _SubImageAt(int x, int y) {
    var oddX = (x & 1) != 0;
    var oddY = (y & 1) != 0;
    return oddY ? oddX ? 2 : 1 : oddX ? 3 : 0;
  }

  private static byte _Red(RawImage image, int x, int y) => image.PixelData[(y * image.Width + x) * 3];

  private static byte _Grey(int luminance) => (byte)Math.Clamp((298 * (luminance - 16) + 128) >> 8, 0, 255);
}
