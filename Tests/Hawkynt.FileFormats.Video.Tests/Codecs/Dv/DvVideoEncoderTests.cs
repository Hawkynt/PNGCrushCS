using System;
using System.IO;
using System.Security.Cryptography;
using FileFormat.Core;

namespace FileFormat.Codecs.Dv.Tests;

/// <summary>
/// The DV encoder, against FFmpeg's own encoder and against what the format's own decoder makes of
/// what it writes.
/// </summary>
/// <remarks>
/// The strongest check here is exact rather than approximate. Handed the same planes, this encoder
/// writes the same bytes as FFmpeg's DV encoder does — the whole frame, control blocks and all — so
/// the test is a digest and not a tolerance. Both fixtures were re-encoded through
/// <c>ffmpeg -cpuflags 0 -f rawvideo … -c:v dvvideo -f avi</c>, which stores the encoder's packets
/// verbatim where the raw DV muxer would have rewritten the timecode and recording-date packs.
/// </remarks>
[TestFixture]
public class DvVideoEncoderTests {

  /// <summary>
  /// The committed frames, re-encoded: the digest of what FFmpeg's DV encoder writes when given the
  /// planes that frame decodes to.
  /// </summary>
  private static readonly (string File, DvSampling Sampling, string Digest, int FrameSize)[] _ReEncoded = [
    ("dv25-525-411.dv", DvSampling.FourOneOne, "71250e798a3fcd8cdc1dc3e4db93d967", 120000),
    ("dvcpro50-525-422.dv", DvSampling.FourTwoTwo, "1483a1c95b4ba3dfd38231672527a0a9", 240000),
  ];

  // ============================================================================================
  // Against FFmpeg
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ReEncodingACommittedFrameWritesWhatFFmpegWrites() {
    foreach (var (file, sampling, digest, frameSize) in _ReEncoded) {
      var planes = _DecodePlanes(file);
      var encoded = _Encoder(720, 480).EncodePlanes(planes, sampling);

      Assert.Multiple(() => {
        Assert.That(encoded, Has.Length.EqualTo(frameSize), file);
        Assert.That(_Digest(encoded), Is.EqualTo(digest),
          $"{file}: the re-encoded frame is not byte for byte what ffmpeg's DV encoder writes for the same planes.");
      });
    }
  }

  // ============================================================================================
  // Against this package's own decoder
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void AFlatPictureSurvivesTheRoundTripExactly() {
    // A flat field puts everything in the first coefficient and nothing in any other, and the first
    // coefficient is coded without loss: the encoder writes (8192 >> 3) - 1024 + 2 >> 2, which is
    // nought for mid-grey, and the decoder reconstructs 1024, which the transform turns back into
    // 128. So this is the one picture DV holds exactly, and it pins the two ends of the scaling
    // convention against each other.
    foreach (var level in new byte[] { 16, 128, 235 }) {
      var planes = _Flat(720, 480, 180, 480, level);
      var encoded = _Encoder(720, 480).EncodePlanes(planes, DvSampling.FourOneOne);
      var decoded = _Decoder().DecodePlanes(encoded, out var profile);

      Assert.Multiple(() => {
        Assert.That(profile.Name, Is.EqualTo("DV25 525/60 4:1:1"));
        Assert.That(decoded.Luma, Is.All.EqualTo(level), $"luma at {level}");
        Assert.That(decoded.Cb, Is.All.EqualTo(level), $"blue difference at {level}");
        Assert.That(decoded.Cr, Is.All.EqualTo(level), $"red difference at {level}");
      });
    }
  }

  [Test]
  [Category("Unit")]
  public void EveryProfileWritesTheFrameSizeItsGeometryFixes() {
    // A DV frame is a fixed length whatever is in it, and the length is the profile's rather than
    // the picture's — which is the property that makes the whole format work and the one a decoder
    // has no way of recovering from if it is wrong.
    foreach (var (width, height, sampling, size, name) in new[] {
      (720, 480, DvSampling.FourOneOne, 120000, "DV25 525/60 4:1:1"),
      (720, 576, DvSampling.FourTwoZero, 144000, "DV25 625/50 4:2:0 (IEC 61834)"),
      (720, 576, DvSampling.FourOneOne, 144000, "DV25 625/50 4:1:1 (SMPTE 314M)"),
      (720, 480, DvSampling.FourTwoTwo, 240000, "DVCPRO50 525/60 4:2:2"),
      (720, 576, DvSampling.FourTwoTwo, 288000, "DVCPRO50 625/50 4:2:2"),
    }) {
      var chromaWidth = sampling == DvSampling.FourOneOne ? width / 4 : width / 2;
      var chromaHeight = sampling == DvSampling.FourTwoZero ? height / 2 : height;
      var encoded = _Encoder(width, height)
        .EncodePlanes(_Flat(width, height, chromaWidth, chromaHeight, 128), sampling);

      // …and the frame has to say which profile it is in, because that is the only place a decoder
      // can learn it from: the four-character code in a container says only that a stream is DV.
      var profile = DvProfile.Identify(encoded);

      Assert.Multiple(() => {
        Assert.That(encoded, Has.Length.EqualTo(size), name);
        Assert.That(profile.Name, Is.EqualTo(name));
        Assert.That(profile.Width, Is.EqualTo(width));
        Assert.That(profile.Height, Is.EqualTo(height));
      });
    }
  }

  [Test]
  [Category("Unit")]
  public void EveryDifBlockOfAWrittenFrameCarriesItsOwnIdentifier() {
    // A DIF block says what it is in its first three bytes, and a player that cannot find the video
    // blocks has nothing to decode. The order is fixed: six control blocks open a sequence, then
    // twenty-seven segments of five video blocks with an audio block before every third.
    var encoded = _Encoder(720, 480).EncodePlanes(_Flat(720, 480, 180, 480, 128), DvSampling.FourOneOne);
    var problems = 0;
    var video = 0;
    var audio = 0;

    for (var sequence = 0; sequence < 10; ++sequence)
      for (var block = 0; block < 150; ++block) {
        var at = (sequence * 150 + block) * 80;
        var expected = block switch {
          0 => 0x1f,       // header
          1 or 2 => 0x3f,  // subcode
          3 or 4 or 5 => 0x56, // video auxiliary
          _ => (block - 6) % 16 == 0 ? 0x76 : 0x96,
        };

        if (encoded[at] != expected)
          ++problems;
        if (encoded[at] == 0x96)
          ++video;
        if (encoded[at] == 0x76)
          ++audio;

        // The sequence number is in the high nibble of the second byte, with the reserved bits set.
        if ((encoded[at + 1] >> 4) != sequence)
          ++problems;
      }

    Assert.Multiple(() => {
      Assert.That(problems, Is.Zero, "a DIF block carries the wrong identifier");
      Assert.That(video, Is.EqualTo(10 * 135), "video DIF blocks in a 525/60 frame");
      Assert.That(audio, Is.EqualTo(10 * 9), "audio DIF blocks in a 525/60 frame");
    });
  }

  // ============================================================================================
  // Identity and refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void ThePacketsAreNamedDvsdWhateverTheProfile() {
    // The code says only that a stream is DV. Every DV decoder reads the profile out of the frame,
    // so writing dv50 on a DVCPRO50 stream would state twice what is already stated once.
    Assert.That(DvVideoEncoder.Codec, Is.EqualTo(CodecTag.FromCharacters("dvsd")));

    var described = _Encoder(720, 576).DescribeStream();
    Assert.Multiple(() => {
      Assert.That(described.Codec, Is.EqualTo(CodecTag.FromCharacters("dvsd")));
      Assert.That(described.Width, Is.EqualTo(720));
      Assert.That(described.Height, Is.EqualTo(576));
    });
  }

  [Test]
  [Category("Unit")]
  public void ARasterDvDoesNotDefineIsRefused() {
    foreach (var (width, height) in new[] { (640, 480), (720, 486), (1440, 1080), (0, 0) }) {
      var failure = Assert.Throws<NotSupportedException>(() => _Encoder(width, height));
      Assert.That(failure.Message, Does.Contain("720x480").And.Contain("720x576"), $"{width}x{height}");
    }
  }

  [Test]
  [Category("Unit")]
  public void AStreamThatWouldChangeFrameSizePartWayIsRefused() {
    // 4:1:1 at 525/60 is 120000 bytes a frame and 4:2:2 is 240000. No container describes a stream
    // whose frames are two different lengths, so the second frame is refused rather than written.
    var encoder = _Encoder(720, 480);
    encoder.EncodePlanes(_Flat(720, 480, 180, 480, 128), DvSampling.FourOneOne);

    var failure = Assert.Throws<NotSupportedException>(
      () => encoder.EncodePlanes(_Flat(720, 480, 360, 480, 128), DvSampling.FourTwoTwo));
    Assert.That(failure.Message, Does.Contain("DV25 525/60 4:1:1").And.Contain("DVCPRO50 525/60 4:2:2"));
  }

  [Test]
  [Category("Unit")]
  public void APictureThatIsNotTheStreamsSizeIsRefused() {
    var encoder = _Encoder(720, 480);
    var picture = new RawImage { Width = 720, Height = 576, Format = PixelFormat.Rgb24, PixelData = new byte[720 * 576 * 3] };

    var failure = Assert.Throws<InvalidDataException>(() => encoder.TryEncode(picture, 0, out _));
    Assert.That(failure.Message, Does.Contain("720x480").And.Contain("720x576"));
  }

  [Test]
  [Category("Unit")]
  public void APackedPictureIsCodedAtTheSamplingItsSystemUsed() {
    // 525/60 recorders wrote 4:1:1 and 625/50 recorders 4:2:0. A picture that says nothing about its
    // own colour sampling — the RGB every decoder here hands back — is written the way a recorder of
    // its system would have written it, and only a 4:2:2 planar picture asks for DVCPRO50.
    foreach (var (width, height, size, name) in new[] {
      (720, 480, 120000, "DV25 525/60 4:1:1"),
      (720, 576, 144000, "DV25 625/50 4:2:0 (IEC 61834)"),
    }) {
      var encoder = _Encoder(width, height);
      var picture = new RawImage {
        Width = width,
        Height = height,
        Format = PixelFormat.Rgb24,
        PixelData = new byte[width * height * 3],
      };

      Assert.That(encoder.TryEncode(picture, 0, out var packet), Is.True, name);
      Assert.Multiple(() => {
        Assert.That(packet.Data.Length, Is.EqualTo(size), name);
        Assert.That(packet.IsKeyFrame, Is.True, "every DV frame is a whole picture");
        Assert.That(DvProfile.Identify(packet.Data.Span).Name, Is.EqualTo(name));
      });
    }
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  private static DvVideoEncoder _Encoder(int width, int height)
    => DvVideoEncoder.Create(new() { Index = 0, Kind = MediaStreamKind.Video, Width = width, Height = height });

  private static DvVideoDecoder _Decoder()
    => DvVideoDecoder.Create(new() { Index = 0, Kind = MediaStreamKind.Video });

  private static DvPlanes _DecodePlanes(string file) => _Decoder().DecodePlanes(_Fixture(file), out _);

  private static DvPlanes _Flat(int width, int height, int chromaWidth, int chromaHeight, byte level) {
    var luma = new byte[width * height];
    var cb = new byte[chromaWidth * chromaHeight];
    var cr = new byte[chromaWidth * chromaHeight];
    Array.Fill(luma, level);
    Array.Fill(cb, level);
    Array.Fill(cr, level);

    return new() {
      Width = width,
      Height = height,
      ChromaWidth = chromaWidth,
      ChromaHeight = chromaHeight,
      Luma = luma,
      Cb = cb,
      Cr = cr,
    };
  }

  private static string _Digest(byte[] data) => Convert.ToHexString(MD5.HashData(data)).ToLowerInvariant();

  private static byte[] _Fixture(string name)
    => File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Dv", name));
}
