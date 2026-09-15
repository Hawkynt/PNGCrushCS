using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FileFormat.Core;

namespace FileFormat.Codecs.Dv.Tests;

/// <summary>The DV decoder against FFmpeg output and the formats' fixed geometry.</summary>
[TestFixture]
public class DvVideoDecoderTests {

  private static readonly (string File, string Digest, string Profile, int ChromaWidth, int ChromaHeight)[] _Frames = [
    ("dv25-525-411.dv", "bce9b7591cd42cd801daf6bc933e54c3", "DV25 525/60 4:1:1", 180, 480),
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

  [Test]
  [Category("Unit")]
  public void EveryProfileIsAWholeNumberOfDifBlocks() {
    foreach (var profile in DvProfile.All)
      Assert.That(
        profile.FrameSize,
        Is.EqualTo(profile.ChannelCount * profile.SequencesPerChannel * 150 * DvProfile.DifBlockSize),
        profile.Name);
  }

  [Test]
  [Category("Unit")]
  public void TheSegmentWalkTilesEverySampleOfThePictureExactlyOnce() {
    foreach (var profile in DvProfile.All) {
      var luma = new int[profile.Width * profile.Height];
      var chroma = new int[profile.ChromaWidth * profile.ChromaHeight];
      var folded = false;

      foreach (var segment in DvGeometry.Segments(profile))
        for (var i = 0; i < DvProfile.MacroblocksPerSegment; ++i) {
          var x = segment.MacroblockX[i];
          var y = segment.MacroblockY[i];

          if (profile.IsDv100) {
            // The ordinary DV100 macroblock is a 16x16 luma square with one 8x16 4:2:2 colour
            // rectangle per component. The 1080-line bottom edge has only eight lines left, so the
            // same four/two blocks turn sideways into 32x8 and 16x8 respectively.
            if (y == 134) {
              _Mark(luma, profile.Width, x * 8, y * 8, 32, 8);
              _Mark(chroma, profile.ChromaWidth, x / 2 * 8, y * 8, 16, 8);
            } else {
              _Mark(luma, profile.Width, x * 8, y * 8, 16, 16);
              _Mark(chroma, profile.ChromaWidth, x / 2 * 8, y * 8, 8, 16);
            }
            continue;
          }

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
    Assert.Multiple(() => {
      Assert.That(DvTables.Zigzag.OrderBy(n => n), Is.EqualTo(Enumerable.Range(0, 64)).AsCollection);
      Assert.That(DvTables.Zigzag248.OrderBy(n => n), Is.EqualTo(Enumerable.Range(0, 64)).AsCollection);
      Assert.That(DvTables.Zigzag[0], Is.Zero);
      Assert.That(DvTables.Zigzag248[0], Is.Zero);
    });
  }

  [Test]
  [Category("Unit")]
  public void TheCodeBookIsACompletePrefixCode() {
    ulong space = 0;
    foreach (var length in DvTables.CodeLengths)
      space += 1ul << (32 - length);
    Assert.That(space, Is.EqualTo(1ul << 32), "the code lengths do not tile the code space");

    for (var window = 0; window < 1 << 16; ++window) {
      var code = DvVlc.Decode((uint)window << 16);
      if (code.Length == 0)
        Assert.Fail($"the sixteen-bit window 0x{window:x4} decodes to no code");
    }
  }

  [Test]
  [Category("Unit")]
  public void TheEndOfBlockStampIsTheFourBitCodeTheEncoderWrites() {
    var code = DvVlc.Decode(0b0110u << 28);
    Assert.Multiple(() => {
      Assert.That(code.Length, Is.EqualTo(4));
      Assert.That(code.Run, Is.EqualTo(127));
      Assert.That(code.Level, Is.Zero);
    });
  }

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
      Assert.That(DvVideoDecoder.Accepts(_Stream("AVdn")), Is.False);
      Assert.That(
        DvVideoDecoder.Accepts(new() { Index = 0, Kind = MediaStreamKind.Audio, Codec = CodecTag.FromCharacters("dvsd") }),
        Is.False);
    });
  }

  [Test]
  [Category("Unit")]
  public void DvcproHdProfilesAreIdentifiedFromTheirSignalTypeAndSystem() {
    foreach (var (signalType, sequenceFlag, expected) in new[] {
      (0x14, 0, DvProfile.DvcproHd1080I60),
      (0x14, 1, DvProfile.DvcproHd1080I50),
      (0x18, 0, DvProfile.DvcproHd720P60),
      (0x18, 1, DvProfile.DvcproHd720P50),
    })
      Assert.That(DvProfile.Identify(_Header(sequenceFlag, signalType)), Is.SameAs(expected), expected.Name);
  }

  [Test]
  [Category("Unit")]
  public void ConsumerIecHdIsStillRefusedByName() {
    foreach (var (sequenceFlag, system) in new[] { (0, "1125/60"), (1, "1250/50") }) {
      var failure = Assert.Throws<NotSupportedException>(() => DvProfile.Identify(_Header(sequenceFlag, 0x02)));
      Assert.That(failure.Message, Does.Contain("IEC 61834-3").And.Contain(system));
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
    Assert.That(failure.Message, Does.Contain("480"));
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
