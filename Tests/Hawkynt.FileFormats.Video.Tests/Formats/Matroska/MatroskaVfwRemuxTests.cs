using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Avi;
using FileFormat.Core;
using FileFormat.Matroska;
using Hawkynt.FileFormats.Video;
using Hawkynt.FileFormats.Video.Tests;

namespace FileFormat.Matroska.Tests;

/// <summary>A VfW codec with no native Matroska CodecID crosses from AVI to Matroska as V_MS/VFW/FOURCC.</summary>
[TestFixture]
public sealed class MatroskaVfwRemuxTests {

  private static (MediaStreamInfo Stream, List<CodedPacket> Packets) _ZmbvClip(int frames) {
    var requested = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("ZMBV"),
      Handler = CodecTag.FromCharacters("ZMBV"),
      Width = 32,
      Height = 16,
      TimeBase = new Rational(1, 25),
      FrameRate = new Rational(25, 1),
    };
    var encoder = VideoFormatRegistry.CreateEncoder(requested);
    var packets = new List<CodedPacket>();
    for (var i = 0; i < frames; ++i) {
      var pixels = new byte[32 * 16 * 3];
      for (var p = 0; p < pixels.Length; ++p)
        pixels[p] = (byte)(p * 7 + i * 31);
      encoder.TryEncode(new RawImage { Width = 32, Height = 16, Format = PixelFormat.Rgb24, PixelData = pixels }, i, out var packet);
      packets.Add(packet);
    }
    return (encoder.DescribeStream(), packets);
  }

  [Test]
  [Category("Unit")]
  public void AnAviStreamRemuxedToMatroskaKeepsItsPacketsAndItsBitmapHeader() {
    var (stream, packets) = _ZmbvClip(3);
    var avi = VideoIO.Mux<AviWriter>([stream], packets);
    var fromAvi = VideoFormatRegistry.ReadStreams(avi).Single();

    var mkv = VideoIO.Mux<MatroskaWriter>([fromAvi], VideoFormatRegistry.ReadPackets(avi));
    var back = VideoFormatRegistry.ReadStreams(mkv).Single();

    Assert.Multiple(() => {
      Assert.That(back.CodecId, Is.EqualTo("V_MS/VFW/FOURCC"));
      Assert.That(back.Codec, Is.EqualTo(CodecTag.FromCharacters("ZMBV")));
      Assert.That(VideoFormatRegistry.ReadPackets(mkv).Select(static p => p.Data.ToArray()),
        Is.EqualTo(packets.Select(static p => p.Data.ToArray())));
    });
  }

  [Test]
  [Category("Unit")]
  public void AVideoStreamWithoutABitmapHeaderIsGivenOne() {
    var (stream, packets) = _ZmbvClip(1);
    var bare = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = stream.Codec,
      Width = stream.Width,
      Height = stream.Height,
      TimeBase = stream.TimeBase,
      FrameRate = stream.FrameRate,
    };
    var mkv = VideoIO.Mux<MatroskaWriter>([bare], packets);
    var back = VideoFormatRegistry.ReadStreams(mkv).Single();
    Assert.Multiple(() => {
      Assert.That(back.CodecId, Is.EqualTo("V_MS/VFW/FOURCC"));
      Assert.That(back.Width, Is.EqualTo(32));
      Assert.That(back.Codec, Is.EqualTo(CodecTag.FromCharacters("ZMBV")));
    });
  }

  [Test]
  [Category("Conformance")]
  public void FFmpegReadsTheRemuxedMatroskaToTheSamePictures() {
    FFmpegOracle.RequireAvailable();
    var (stream, packets) = _ZmbvClip(4);
    var avi = VideoIO.Mux<AviWriter>([stream], packets);
    var mkv = VideoIO.Mux<MatroskaWriter>([VideoFormatRegistry.ReadStreams(avi).Single()], VideoFormatRegistry.ReadPackets(avi));

    var directory = Directory.CreateTempSubdirectory("vfwremux");
    try {
      var aviPath = Path.Combine(directory.FullName, "clip.avi");
      var mkvPath = Path.Combine(directory.FullName, "clip.mkv");
      File.WriteAllBytes(aviPath, avi);
      File.WriteAllBytes(mkvPath, mkv);
      var (fromAvi, aviDetail, aviPictures) = FFmpegOracle.TryDecodePictures(aviPath, 32, 16, 4);
      var (fromMkv, mkvDetail, mkvPictures) = FFmpegOracle.TryDecodePictures(mkvPath, 32, 16, 4);
      Assert.That(fromAvi, Is.True, aviDetail);
      Assert.That(fromMkv, Is.True, mkvDetail);
      Assert.That(mkvPictures, Is.EqualTo(aviPictures));
    } finally {
      directory.Delete(recursive: true);
    }
  }
}
