using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FileFormat.Core;
using Hawkynt.FileFormats.Images;
using Hawkynt.FileFormats.Video;

namespace Crush.Viewer.Tests;

/// <summary>Opening a video as pages, stepping through it, and writing it back as video or as pictures.</summary>
[TestFixture]
public sealed class VideoServiceTests {

  private const int _WIDTH = 16;
  private const int _HEIGHT = 8;
  private const int _FRAMES = 6;

  /// <summary>A short lossless film: ZMBV in AVI, a key frame every so often, frames all different.</summary>
  private static (FileInfo File, List<RawImage> Pictures) _Film(DirectoryInfo folder, string name, VideoFormat container = VideoFormat.Avi) {
    var pictures = Enumerable.Range(0, _FRAMES)
      .Select(static i => ViewerTestPictures.Flat(_WIDTH, _HEIGHT, (byte)(i * 40), (byte)(255 - i * 30), (byte)(i * 7)))
      .ToList();

    var requested = new MediaStreamInfo {
      Index = 0,
      Kind = MediaStreamKind.Video,
      Codec = CodecTag.FromCharacters("ZMBV"),
      Handler = CodecTag.FromCharacters("ZMBV"),
      Width = _WIDTH,
      Height = _HEIGHT,
      TimeBase = new Rational(1, 10),
      FrameRate = new Rational(10, 1),
    };
    var encoder = VideoFormatRegistry.CreateEncoder(requested);
    var packets = new List<CodedPacket>();
    for (var i = 0; i < pictures.Count; ++i) {
      Assert.That(encoder.TryEncode(pictures[i], i, out var packet), Is.True);
      packets.Add(packet);
    }
    packets.AddRange(encoder.Flush());

    var file = new FileInfo(Path.Combine(folder.FullName, name));
    File.WriteAllBytes(file.FullName, VideoFormatRegistry.Mux(container, [encoder.DescribeStream()], packets));
    return (file, pictures);
  }

  private static List<byte[]> _Frames(FileInfo file)
    => VideoFormatRegistry.DecodeFrames(new FileInfo(file.FullName)).Select(static f => f.Image.ToRgb24()).ToList();

  [Test]
  public void AVideoOpensWithOneFramePerPictureAndItsOwnFrameRate() {
    using var scratch = new ScratchFolder("video-open");
    var (file, pictures) = _Film(scratch.Directory, "clip.avi");

    var video = VideoService.Open(file);
    Assert.That(video, Is.Not.Null);
    Assert.Multiple(() => {
      Assert.That(video!.FrameCount, Is.EqualTo(_FRAMES));
      Assert.That(video.FramesPerSecond, Is.EqualTo(10).Within(0.001));
      Assert.That(video.CodecName, Does.Contain("Zip Motion Blocks"));
      Assert.That(PageService.PageCount(file, null), Is.EqualTo(_FRAMES));
      for (var i = 0; i < _FRAMES; ++i)
        Assert.That(PageService.LoadPage(file, null, i)!.ToRgb24(), Is.EqualTo(pictures[i].ToRgb24()), $"frame {i}");
    });
  }

  [Test]
  public void SteppingBackRestartsAtAKeyFrameAndGivesTheSamePictures() {
    using var scratch = new ScratchFolder("video-seek");
    var (file, pictures) = _Film(scratch.Directory, "clip.avi");
    var video = VideoService.Open(file)!;

    // Forward to the end, then back to the start and into the middle.
    Assert.That(video.Frame(_FRAMES - 1)!.ToRgb24(), Is.EqualTo(pictures[^1].ToRgb24()));
    Assert.That(video.Frame(0)!.ToRgb24(), Is.EqualTo(pictures[0].ToRgb24()));
    Assert.That(video.Frame(3)!.ToRgb24(), Is.EqualTo(pictures[3].ToRgb24()));
    Assert.That(video.Frame(2)!.ToRgb24(), Is.EqualTo(pictures[2].ToRgb24()));
    Assert.That(video.Frame(_FRAMES), Is.Null);
  }

  [Test]
  public void TheFolderListsAVideoByItsBytesEvenUnderAForeignName() {
    using var scratch = new ScratchFolder("video-list");
    _Film(scratch.Directory, "clip.avi");
    var (renamed, _) = _Film(scratch.Directory, "clip-renamed.qqqvid");
    File.WriteAllText(Path.Combine(scratch.Directory.FullName, "notes.qqqvid"), "not a video at all");

    var model = new FolderModel();
    model.Open(scratch.Directory);
    var names = model.Files.Select(static f => f.Name).ToList();
    Assert.Multiple(() => {
      Assert.That(names, Does.Contain("clip.avi"));
      Assert.That(names, Does.Contain(renamed.Name));
      Assert.That(names, Does.Not.Contain("notes.qqqvid"));
    });
  }

  [Test]
  public void ChangingOnlyTheContainerCopiesThePacketsUnchanged() {
    using var scratch = new ScratchFolder("video-remux");
    var (file, _) = _Film(scratch.Directory, "clip.avi");
    var target = new FileInfo(Path.Combine(scratch.Directory.FullName, "out", "clip.mkv"));

    var result = VideoService.Convert(file, VideoFormat.Matroska, codec: null, target, overwrite: false);
    Assert.That(result.Succeeded, Is.True, result.Message);

    var before = VideoFormatRegistry.ReadPackets(File.ReadAllBytes(file.FullName)).Select(static p => p.Data.ToArray()).ToList();
    var after = VideoFormatRegistry.ReadPackets(File.ReadAllBytes(target.FullName)).Select(static p => p.Data.ToArray()).ToList();
    Assert.Multiple(() => {
      Assert.That(VideoFormatRegistry.Detect(new FileInfo(target.FullName)), Is.EqualTo(VideoFormat.Matroska));
      Assert.That(after, Is.EqualTo(before));
      Assert.That(_Frames(target), Is.EqualTo(_Frames(file)));
    });
  }

  [Test]
  public void ChangingTheCodecReEncodesEveryFrame() {
    using var scratch = new ScratchFolder("video-transcode");
    var (file, pictures) = _Film(scratch.Directory, "clip.avi");
    var target = new FileInfo(Path.Combine(scratch.Directory.FullName, "clip-huffyuv.avi"));

    var result = VideoService.Convert(file, VideoFormat.Avi, CodecTag.FromCharacters("HFYU"), target, overwrite: false);
    Assert.That(result.Succeeded, Is.True, result.Message);

    var streams = VideoFormatRegistry.ReadStreams(File.ReadAllBytes(target.FullName));
    Assert.Multiple(() => {
      Assert.That(streams.Single().Codec, Is.EqualTo(CodecTag.FromCharacters("HFYU")));
      Assert.That(_Frames(target), Is.EqualTo(pictures.Select(static p => p.ToRgb24()).ToList()));
    });
  }

  [Test]
  public void ACodecWithoutAnEncoderIsRefusedByNameAndWritesNothing() {
    using var scratch = new ScratchFolder("video-refuse");
    var (file, _) = _Film(scratch.Directory, "clip.avi");
    var target = new FileInfo(Path.Combine(scratch.Directory.FullName, "clip-none.avi"));

    var result = VideoService.Convert(file, VideoFormat.Avi, CodecTag.FromCharacters("ZZZZ"), target, overwrite: false);
    target.Refresh();
    Assert.Multiple(() => {
      Assert.That(result.Succeeded, Is.False);
      Assert.That(result.Message, Is.Not.Empty);
      Assert.That(target.Exists, Is.False);
      Assert.That(File.Exists(target.FullName + ".partial"), Is.False);
    });
  }

  [Test]
  public void AnExistingFileIsKeptUnlessReplacingWasAskedFor() {
    using var scratch = new ScratchFolder("video-overwrite");
    var (file, _) = _Film(scratch.Directory, "clip.avi");
    var target = new FileInfo(Path.Combine(scratch.Directory.FullName, "taken.mkv"));
    File.WriteAllText(target.FullName, "keep me");

    Assert.That(VideoService.Convert(file, VideoFormat.Matroska, null, target, overwrite: false).Succeeded, Is.False);
    Assert.That(File.ReadAllText(target.FullName), Is.EqualTo("keep me"));
    Assert.That(VideoService.Convert(file, VideoFormat.Matroska, null, target, overwrite: true).Succeeded, Is.True);
  }

  [Test]
  public void ExtractingPagesWritesEveryFrameAsAPicture() {
    using var scratch = new ScratchFolder("video-extract");
    var (file, pictures) = _Film(scratch.Directory, "clip.avi");
    var png = FormatRegistry.GetEntry(ImageFormat.Png)!;
    var destination = scratch.Sub("frames");

    var results = PageService.ExtractPages(file, null, png, destination, overwrite: false);
    var written = destination.GetFiles("*.png").OrderBy(static f => f.Name).ToList();
    Assert.Multiple(() => {
      Assert.That(results.All(static r => r.Succeeded), Is.True);
      Assert.That(written, Has.Count.EqualTo(_FRAMES));
      for (var i = 0; i < _FRAMES; ++i)
        Assert.That(FormatRegistry.Read(written[i])!.ToRgb24(), Is.EqualTo(pictures[i].ToRgb24()), written[i].Name);
    });
  }

  [Test]
  public void ConvertingAVideoToAPictureWritesItsFirstFrame() {
    using var scratch = new ScratchFolder("video-to-picture");
    var (file, pictures) = _Film(scratch.Directory, "clip.avi");
    var target = new FileInfo(Path.Combine(scratch.Directory.FullName, "first.png"));

    var result = ConversionService.Convert(file, FormatRegistry.GetEntry(ImageFormat.Png)!, target, overwrite: false);
    Assert.That(result.Succeeded, Is.True, result.Message);
    Assert.That(FormatRegistry.Read(new FileInfo(target.FullName))!.ToRgb24(), Is.EqualTo(pictures[0].ToRgb24()));
  }

  [Test]
  public void EveryContainerTheConvertDialogOffersCanBeWritten() {
    Assert.That(VideoService.WritableContainers, Is.Not.Empty);
    Assert.That(VideoService.WritableContainers.All(static e => e.SupportsWrite), Is.True);
    Assert.That(VideoService.Encoders.Select(static e => e.CodecName), Does.Contain("Duck TrueMotion 1"));
  }
}
