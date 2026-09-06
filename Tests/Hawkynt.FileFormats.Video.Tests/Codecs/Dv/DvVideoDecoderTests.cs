using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FileFormat.Core;

namespace FileFormat.Codecs.Dv.Tests;

/// <summary>
/// The DV decoder, against frames FFmpeg's own encoder wrote and against the format's fixed geometry.
/// </summary>
/// <remarks>
/// The arithmetic was checked against FFmpeg over all six standard-definition profiles, on the
/// planes, sample for sample: synthetic frames at 720x480 and 720x576 in 4:1:1, 4:2:0 and 4:2:2, both
/// transform modes, plus real recordings from the FFmpeg sample archive. Nothing differed anywhere,
/// which is what the two committed frames pin down here.
/// <para/>
/// What the rest of these tests add is what a sample comparison cannot reach: that the segment walk
/// tiles the picture rather than merely producing one, that the code book is a complete prefix code,
/// that each profile's frame is a whole number of DIF blocks, and that the arrangements this decoder
/// does not read are refused by name.
/// </remarks>
[TestFixture]
public class DvVideoDecoderTests {

  // ============================================================================================
  // Against FFmpeg
  // ============================================================================================

  /// <summary>
  /// The two committed frames, with the digest of FFmpeg's own decode of each.
  /// </summary>
  /// <remarks>
  /// Each frame is one picture of <c>testsrc2</c> — a pattern with flat fields, hard edges and fine
  /// detail, so that blocks which fit their bit budget and blocks which overrun it into two levels of
  /// shared space both occur in the same frame. The digest is over the three planes concatenated as
  /// FFmpeg writes them with <c>-f rawvideo</c>, which is what a plane-level comparison amounts to
  /// once it has to be committed rather than run.
  /// </remarks>
  private static readonly (string File, string Digest, string Profile, int ChromaWidth, int ChromaHeight)[] _Frames = [
    // ffmpeg -f lavfi -i testsrc2=size=720x480:rate=30000/1001 -frames:v 1 -c:v dvvideo -pix_fmt yuv411p
    ("dv25-525-411.dv", "bce9b7591cd42cd801daf6bc933e54c3", "DV25 525/60 4:1:1", 180, 480),
    // ffmpeg -f lavfi -i testsrc2=size=720x480:rate=30000/1001 -frames:v 1 -c:v dvvideo -pix_fmt yuv422p
    ("dvcpro50-525-422.dv", "202cd8dfec4667c42290473278eedef9", "DVCPRO50 525/60 4:2:2", 360, 480),
  ];

  [Test]
  [Category("Unit")]
  public void EveryCommittedFrameDecodesToWhatFFmpegDecodesItTo() {
    foreach (var (file, digest, profileName, chromaWidth, chromaHeight) in _Frames) {
      var planes = _DecodePlanes(_Fixture(file), out var profile);

      Assert.Multiple(() => {
        Assert.That(profile.Name, Is.EqualTo(profileName), file);
        Assert.That(profile.Width, Is.EqualTo(720), file);
        Assert.That(profile.Height, Is.EqualTo(480), file);
        Assert.That(planes.ChromaWidth, Is.EqualTo(chromaWidth), file);
        Assert.That(planes.ChromaHeight, Is.EqualTo(chromaHeight), file);
        Assert.That(_Digest(planes), Is.EqualTo(digest),
          $"{file}: the decoded planes are not the ones ffmpeg decodes this frame to.");
      });
    }
  }

  [Test]
  [Category("Unit")]
  public void TheDecodedPictureIsTheRasterTheProfileStates() {
    var decoder = DvVideoDecoder.Create(new() { Index = 0, Kind = MediaStreamKind.Video });

    Assert.That(decoder.TryDecode(_Packet(_Fixture("dv25-525-411.dv")), out var frame), Is.True);
    Assert.Multiple(() => {
      Assert.That(frame.Width, Is.EqualTo(720));
      Assert.That(frame.Height, Is.EqualTo(480));
      Assert.That(frame.Format, Is.EqualTo(PixelFormat.Rgb24));
      Assert.That(frame.PixelData.Length, Is.EqualTo(720 * 480 * 3));
    });
  }

  // ============================================================================================
  // The geometry, which is fixed and therefore checkable
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void EveryProfileIsAWholeNumberOfDifBlocks() {
    // A DIF sequence is 150 blocks of 80 bytes: six of control data, then twenty-seven video segments
    // of five blocks with nine audio blocks interleaved. Nothing about a frame size is free.
    foreach (var profile in DvProfile.All)
      Assert.That(
        profile.FrameSize,
        Is.EqualTo(profile.ChannelCount * profile.SequencesPerChannel * 150 * DvProfile.DifBlockSize),
        profile.Name);
  }

  [Test]
  [Category("Unit")]
  public void TheSegmentWalkTilesEverySampleOfThePictureExactlyOnce() {
    // The five macroblocks of a segment come from five widely separated parts of the picture and the
    // segments walk it in a serpentine, so "it produced a picture" says nothing. What has to hold is
    // that the blocks tile the raster: every sample written once, none twice, none outside. The
    // placement is restated here from the shapes of the three samplings rather than called, so a
    // change to either side has to be a change to both.
    foreach (var profile in DvProfile.All) {
      var luma = new int[profile.Width * profile.Height];
      var chroma = new int[profile.ChromaWidth * profile.ChromaHeight];
      var folded = false;

      foreach (var segment in DvGeometry.Segments(profile))
        for (var i = 0; i < DvProfile.MacroblocksPerSegment; ++i) {
          var x = segment.MacroblockX[i];
          var y = segment.MacroblockY[i];

          // Past luma column 704 a 4:1:1 macroblock has only sixteen columns left, so its four luma
          // blocks fold into a square and its colour block is split into two half-width halves.
          var square = profile.Sampling == DvSampling.FourTwoZero
            || (profile.Sampling == DvSampling.FourOneOne && x >= 704 / 8);
          folded |= profile.Sampling == DvSampling.FourOneOne && x >= 704 / 8;

          if (profile.Sampling == DvSampling.FourTwoTwo) {
            _Mark(luma, profile.Width, x * 8, y * 8, 16, 8);
            _Mark(chroma, profile.ChromaWidth, x / 2 * 8, y * 8, 8, 8);
            continue;
          }

          if (square) {
            _Mark(luma, profile.Width, x * 8, y * 8, 16, 16);
            if (profile.Sampling == DvSampling.FourTwoZero)
              _Mark(chroma, profile.ChromaWidth, x / 2 * 8, y / 2 * 8, 8, 8);
            else
              // The colour block's left half covers the upper sixteen lines and its right half the
              // lower, four samples wide apiece.
              _Mark(chroma, profile.ChromaWidth, x / 4 * 8, y * 8, 4, 16);
            continue;
          }

          _Mark(luma, profile.Width, x * 8, y * 8, 32, 8);
          _Mark(chroma, profile.ChromaWidth, x / 4 * 8, y * 8, 8, 8);
        }

      Assert.Multiple(() => {
        Assert.That(luma, Is.All.EqualTo(1), $"{profile.Name}: the luma plane is not tiled exactly once.");
        Assert.That(chroma, Is.All.EqualTo(1), $"{profile.Name}: a colour plane is not tiled exactly once.");
        if (profile.Sampling == DvSampling.FourOneOne)
          Assert.That(folded, Is.True, $"{profile.Name}: the last macroblock column is never reached.");
      });
    }
  }

  [Test]
  [Category("Unit")]
  public void BothScanOrdersArePermutationsOfTheBlock() {
    // A scan that repeats a position leaves another one at zero, which decodes to a picture that is
    // merely slightly wrong.
    Assert.Multiple(() => {
      Assert.That(DvTables.Zigzag.OrderBy(n => n), Is.EqualTo(Enumerable.Range(0, 64)).AsCollection);
      Assert.That(DvTables.Zigzag248.OrderBy(n => n), Is.EqualTo(Enumerable.Range(0, 64)).AsCollection);
      Assert.That(DvTables.Zigzag[0], Is.Zero, "the first coefficient of either scan is the DC");
      Assert.That(DvTables.Zigzag248[0], Is.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void TheCodeBookIsACompletePrefixCode() {
    // Canonical assignment over the 409 lengths has to consume exactly the whole code space. One
    // length wrong and every code after it shifts, which is not a decode that fails — it is a decode
    // that produces a different picture.
    ulong space = 0;
    foreach (var length in DvTables.CodeLengths)
      space += 1ul << (32 - length);

    Assert.That(space, Is.EqualTo(1ul << 32), "the code lengths do not tile the code space");

    // …and with the sign bit folded in, every sixteen-bit window must name a code, because a DV block
    // is read to its budget and the bits past the last codeword are whatever the encoder padded with.
    for (var window = 0; window < 1 << 16; ++window) {
      var code = DvVlc.Decode((uint)window << 16);
      if (code.Length == 0)
        Assert.Fail($"the sixteen-bit window 0x{window:x4} decodes to no code");
    }
  }

  [Test]
  [Category("Unit")]
  public void TheEndOfBlockStampIsTheFourBitCodeTheEncoderWrites() {
    // 0110, and a run that walks past the end of the block twice over. Both halves matter: the code
    // is what an encoder emits, and the run is how a decoder knows the block is over.
    var code = DvVlc.Decode(0b0110u << 28);

    Assert.Multiple(() => {
      Assert.That(code.Length, Is.EqualTo(4));
      Assert.That(code.Run, Is.EqualTo(127));
      Assert.That(code.Level, Is.Zero);
    });
  }

  // ============================================================================================
  // Identity
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void TheCodesDvIsNamedByAreAllAccepted() {
    foreach (var code in new[] {
      "dvsd", "DVSD", "dv25", "DV25", "dv50", "DV50", "cdvc", "CDVC", "CDV5", "dvis", "DVIS", "pdvc", "PDVC",
      "dvsl", "SL25", "SLDV", "dvc ", "dvcp", "dvcs", "dvl ", "dvlp", "dvpp", "dv5n", "dv5p", "AVdv",
      "dvhd", "dvh1", "dvh2", "dvh3", "dvh4", "dvh5", "dvh6", "dvhq", "dvhp", "CDVH",
    })
      Assert.That(DvVideoDecoder.Accepts(_Stream(code)), Is.True, code);

    Assert.Multiple(() => {
      Assert.That(DvVideoDecoder.Accepts(_Stream("MJPG")), Is.False);
      Assert.That(DvVideoDecoder.Accepts(_Stream("AVdn")), Is.False, "DNxHD is a different codec");
      Assert.That(
        DvVideoDecoder.Accepts(new() { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("dvsd") }),
        Is.False,
        "an audio stream is never DV video");
    });
  }

  // ============================================================================================
  // Refusals
  // ============================================================================================

  [Test]
  [Category("Unit")]
  public void DvcproHdIsRefusedByName() {
    // Its macroblocks carry eight blocks rather than six and its quantiser is a different table, so
    // reading one as the standard-definition profile with the same flags would hand back a picture.
    foreach (var (signalType, sequenceFlag, name) in new[] {
      (0x14, 0, "1080i60"), (0x14, 1, "1080i50"), (0x18, 0, "720p60"), (0x18, 1, "720p50"),
    }) {
      var frame = _Header(sequenceFlag, signalType);
      var failure = Assert.Throws<NotSupportedException>(() => DvProfile.Identify(frame));
      Assert.That(failure.Message, Does.Contain(name).And.Contain("DVCPRO HD"));
    }
  }

  [Test]
  [Category("Unit")]
  public void ASignalTypeNoStandardDefinesIsRefused() {
    var failure = Assert.Throws<InvalidDataException>(() => DvProfile.Identify(_Header(0, 0x0d)));
    Assert.That(failure.Message, Does.Contain("13").And.Contain("525/60"));
  }

  [Test]
  [Category("Unit")]
  public void APacketTooShortToStateAProfileIsRefused() {
    var failure = Assert.Throws<InvalidDataException>(() => DvProfile.Identify(new byte[100]));
    Assert.That(failure.Message, Does.Contain("480"), "the six DIF blocks a profile is read from");
  }

  [Test]
  [Category("Unit")]
  public void APacketShorterThanTheProfileItClaimsIsRefused() {
    var decoder = DvVideoDecoder.Create(new() { Index = 0, Kind = MediaStreamKind.Video });
    var truncated = _Fixture("dv25-525-411.dv")[..60000];

    var failure = Assert.Throws<InvalidDataException>(() => decoder.DecodePlanes(truncated, out _));
    Assert.That(failure.Message, Does.Contain("120000").And.Contain("60000"));
  }

  [Test]
  [Category("Unit")]
  public void AFrameWhoseRasterIsNotTheContainersIsRefused() {
    var decoder = DvVideoDecoder.Create(new() { Index = 0, Kind = MediaStreamKind.Video, Width = 720, Height = 576 });

    var failure = Assert.Throws<InvalidDataException>(() => decoder.DecodePlanes(_Fixture("dv25-525-411.dv"), out _));
    Assert.That(failure.Message, Does.Contain("720x480").And.Contain("720x576"));
  }

  // ============================================================================================
  // Helpers
  // ============================================================================================

  /// <summary>Counts one more write over a rectangle of a plane, failing loudly outside it.</summary>
  private static void _Mark(int[] plane, int stride, int left, int top, int width, int height) {
    for (var y = top; y < top + height; ++y)
      for (var x = left; x < left + width; ++x) {
        var at = y * stride + x;
        Assert.That(at, Is.InRange(0, plane.Length - 1), $"a block falls outside the plane at ({x}, {y})");
        Assert.That(x, Is.LessThan(stride), $"a block runs past the end of a line at ({x}, {y})");
        ++plane[at];
      }
  }

  private static DvPlanes _DecodePlanes(byte[] frame, out DvProfile profile) {
    var decoder = DvVideoDecoder.Create(new() { Index = 0, Kind = MediaStreamKind.Video });
    return decoder.DecodePlanes(frame, out profile);
  }

  private static string _Digest(DvPlanes planes) {
    using var md5 = MD5.Create();
    md5.TransformBlock(planes.Luma, 0, planes.Luma.Length, null, 0);
    md5.TransformBlock(planes.Cb, 0, planes.Cb.Length, null, 0);
    md5.TransformFinalBlock(planes.Cr, 0, planes.Cr.Length);
    return Convert.ToHexString(md5.Hash!).ToLowerInvariant();
  }

  /// <summary>The six DIF blocks a profile is identified from, stating one system and signal type.</summary>
  private static byte[] _Header(int sequenceFlag, int signalType) {
    var frame = new byte[DvProfile.IdentificationBytes];
    frame[3] = (byte)(sequenceFlag << 7);
    frame[80 * 5 + 48 + 3] = (byte)signalType;
    return frame;
  }

  private static MediaStreamInfo _Stream(string code)
    => new() { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters(code) };

  private static CodedPacket _Packet(byte[] frame) => new(0, frame, IsKeyFrame: true);

  private static byte[] _Fixture(string name)
    => File.ReadAllBytes(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Dv", name));
}
