using System;
using System.IO;
using System.Linq;
using FileFormat.Avi.Tests;
using FileFormat.Core;
using FileFormat.Mjpeg;

namespace Hawkynt.FileFormats.Video.Tests;

/// <summary>
/// The source-generated registry: that containers and codecs are discovered, that they are two
/// separate tables, and that nothing in the path uses reflection to get at either.
/// </summary>
[TestFixture]
public sealed class VideoFormatRegistryTests {

  [Test]
  [Category("Unit")]
  public void EveryContainerIsRegistered() {
    var names = VideoFormatRegistry.AllFormats.Select(e => e.Format).ToList();

    Assert.That(names, Does.Contain(VideoFormat.Avi));
    Assert.That(names, Does.Contain(VideoFormat.Mjpeg));
  }

  [Test]
  [Category("Unit")]
  public void EveryCodecIsRegistered() {
    var names = VideoFormatRegistry.AllCodecs.Select(c => c.CodecName).ToList();

    Assert.That(names, Does.Contain("Motion JPEG"));
    Assert.That(names, Does.Contain("Uncompressed (BI_RGB)"));
  }

  [Test]
  [Category("Unit")]
  public void AnAviIsDetectedByItsBytes() {
    var container = AviTestContainer.Build("MJPG", 8, 4, 24, [AviReaderTests._Jpeg(0)]);

    Assert.That(VideoFormatRegistry.Detect(container), Is.EqualTo(VideoFormat.Avi));
  }

  [Test]
  [Category("Unit")]
  public void AWaveIsNotAnAvi() {
    // Both are RIFF. Only the form type at offset 8 tells them apart, which is why the container
    // gets an opinion of its own rather than a four-byte signature.
    var wave = AviTestContainer.Build("MJPG", 8, 4, 24, [AviReaderTests._Jpeg(0)]);
    wave[8] = (byte)'W';
    wave[9] = (byte)'A';
    wave[10] = (byte)'V';
    wave[11] = (byte)'E';

    Assert.That(VideoFormatRegistry.Detect(wave), Is.EqualTo(VideoFormat.Unknown));
  }

  [Test]
  [Category("Unit")]
  public void ARawMotionJpegStreamIsNotDetectedByItsBytes() {
    // Deliberately. A single-frame .mjpg is a valid JPEG byte for byte, so a signature here would
    // claim every photograph in existence.
    Assert.That(VideoFormatRegistry.Detect(AviReaderTests._Jpeg(0)), Is.EqualTo(VideoFormat.Unknown));
  }

  [Test]
  [Category("Unit")]
  public void AContainerWithoutASignatureIsStillReachedByItsName() {
    var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".mjpg");
    try {
      File.WriteAllBytes(path, AviReaderTests._Jpeg(0));

      Assert.That(VideoFormatRegistry.DecodeFrames(new FileInfo(path)).Count(), Is.EqualTo(1));
    } finally {
      File.Delete(path);
    }
  }

  [Test]
  [Category("Unit")]
  public void ExtensionsAndMediaTypesReachTheirContainers() {
    Assert.That(VideoFormatRegistry.ByExtension(".avi"), Does.Contain(VideoFormat.Avi));
    Assert.That(VideoFormatRegistry.ByExtension("mjpeg"), Does.Contain(VideoFormat.Mjpeg));
    Assert.That(VideoFormatRegistry.ByMimeType("video/x-msvideo"), Is.EqualTo(VideoFormat.Avi));
    Assert.That(VideoFormatRegistry.ByMimeType("video/x-motion-jpeg"), Is.EqualTo(VideoFormat.Mjpeg));
  }

  [Test]
  [Category("Unit")]
  public void ACodecIsChosenByTheStreamsTagAndNothingElse() {
    var motionJpeg = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.FromCharacters("MJPG") };
    var uncompressed = new MediaStreamInfo { Index = 0, Kind = MediaStreamKind.Video, Codec = CodecTag.None };

    Assert.That(VideoFormatRegistry.CanDecode(motionJpeg), Is.True);
    Assert.That(VideoFormatRegistry.CanDecode(uncompressed), Is.True);
    Assert.That(VideoFormatRegistry.CreateDecoder(motionJpeg), Is.InstanceOf<FileFormat.Codecs.MotionJpegDecoder>());
  }

  [Test]
  [Category("Unit")]
  public void AnUnknownCodecIsRefusedByItsFourCharacterCode() {
    var stream = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("H264"),
      Handler = CodecTag.FromCharacters("h264"),
    };

    Assert.That(VideoFormatRegistry.CanDecode(stream), Is.False);

    var failure = Assert.Throws<NotSupportedException>(() => VideoFormatRegistry.CreateDecoder(stream));
    Assert.That(failure!.Message, Does.Contain("H264"));
    Assert.That(failure.Message, Does.Contain("h264"));
  }

  [Test]
  [Category("Unit")]
  public void ADecoderIsBuiltFreshForEachWalk() {
    // Decoders carry state for the codecs that need it, so two walks of the same stream must not
    // share one — the second would begin holding whatever the first left behind.
    var container = MjpegReader.FromBytes(AviReaderTests._Jpeg(0));
    var stream = MjpegContainer.Streams(container)[0];

    var first = VideoIO.Decode(MjpegContainer.ReadPackets(container), stream, VideoFormatRegistry.CreateDecoder);

    Assert.That(first.Count(), Is.EqualTo(1));
    Assert.That(first.Count(), Is.EqualTo(1), "a second enumeration walks the same stream again");
  }
}
