using System.IO;
using System.Linq;
using Hawkynt.FileFormats.Images;

namespace Crush.Viewer.Tests;

/// <summary>What a conversion names its outputs, and when it refuses to write one.</summary>
[TestFixture]
public sealed class ConversionServiceTests {

  private static FormatEntry _Bmp() => FormatRegistry.GetEntry(ImageFormat.Bmp)!;

  [Test]
  public void ConvertBatch_TwoSourcesSharingAStem_WritesTwoFiles() {
    using var scratch = new ScratchFolder("batch-stem");
    var source = scratch.Sub("in");
    var destination = scratch.Sub("out");

    // Given two pictures whose names differ only in their extension, and which are visibly
    // different sizes so the report cannot be believed without looking at what was written.
    var png = ViewerTestPictures.WritePng(source, "shot.png", 8, 4, 0, 0, 255);
    var tga = new FileInfo(Path.Combine(source.FullName, "shot.tga"));
    Assert.That(FormatRegistry.Write(ViewerTestPictures.Flat(16, 6, 255, 0, 0), ImageFormat.Tga, tga), Is.True);

    // When both are converted into one folder.
    var results = ConversionService.ConvertBatch([png, tga], _Bmp(), destination, overwrite: true);

    // Then both were written, to two different files, and both survive on disk.
    Assert.That(results.Where(r => !r.Succeeded).Select(r => r.Message), Is.Empty);
    var targets = results.Select(r => r.Target).ToArray();
    Assert.That(targets[0], Is.Not.EqualTo(targets[1]));

    destination.Refresh();
    var written = destination.GetFiles().OrderBy(f => f.Name).ToArray();
    Assert.That(written, Has.Exactly(2).Items, "one source silently replaced the other");

    var sizes = written.Select(f => FormatRegistry.Read(f)!).Select(i => (i.Width, i.Height)).ToArray();
    Assert.That(sizes, Does.Contain((8, 4)));
    Assert.That(sizes, Does.Contain((16, 6)));
  }

  [Test]
  public void ConvertBatch_ThreeSourcesSharingAStem_FallsBackToANumberedName() {
    using var scratch = new ScratchFolder("batch-three");
    var source = scratch.Sub("in");
    var destination = scratch.Sub("out");

    var first = ViewerTestPictures.WritePng(source, "shot.png", 4, 4, 0, 0, 255);
    var second = new FileInfo(Path.Combine(source.FullName, "shot.tga"));
    Assert.That(FormatRegistry.Write(ViewerTestPictures.Flat(4, 4, 255, 0, 0), ImageFormat.Tga, second), Is.True);
    var third = new FileInfo(Path.Combine(source.FullName, "shot.pcx"));
    Assert.That(FormatRegistry.Write(ViewerTestPictures.Flat(4, 4, 0, 255, 0), ImageFormat.Pcx, third), Is.True);

    var results = ConversionService.ConvertBatch([first, second, third], _Bmp(), destination, overwrite: true);

    Assert.That(results.Where(r => !r.Succeeded).Select(r => r.Message), Is.Empty);
    Assert.That(results.Select(r => r.Target).Distinct(), Has.Exactly(3).Items);
    destination.Refresh();
    Assert.That(destination.GetFiles(), Has.Length.EqualTo(3));
  }

  [Test]
  public void Convert_DestinationCreatedAfterTheFileInfoWas_IsStillRefusedWithoutOverwrite() {
    using var scratch = new ScratchFolder("stale-exists");
    var source = ViewerTestPictures.WritePng(scratch.Directory, "source.png", 4, 4, 10, 20, 30);

    // Given a destination handle taken while nothing was there — and asked, so the answer is cached.
    var destination = new FileInfo(Path.Combine(scratch.Directory.FullName, "target.bmp"));
    Assert.That(destination.Exists, Is.False);

    // And given something has since been written there.
    File.WriteAllBytes(destination.FullName, [0x42, 0x4D, 0x00, 0x00]);
    var before = File.ReadAllBytes(destination.FullName);

    // When a conversion that may not overwrite is pointed at that handle.
    var result = ConversionService.Convert(source, _Bmp(), destination, overwrite: false);

    // Then it refuses, and what was there is untouched.
    Assert.That(result.Succeeded, Is.False);
    Assert.That(result.Message, Does.Contain("already there"));
    Assert.That(File.ReadAllBytes(destination.FullName), Is.EqualTo(before));
  }

  [Test]
  public void Convert_DestinationIsThereAndOverwritingIsAllowed_WritesOverIt() {
    using var scratch = new ScratchFolder("overwrite");
    var source = ViewerTestPictures.WritePng(scratch.Directory, "source.png", 6, 3, 10, 20, 30);
    var destination = new FileInfo(Path.Combine(scratch.Directory.FullName, "target.bmp"));
    File.WriteAllBytes(destination.FullName, [0x42, 0x4D, 0x00, 0x00]);

    var result = ConversionService.Convert(source, _Bmp(), destination, overwrite: true);

    Assert.That(result.Succeeded, Is.True, result.Message);
    destination.Refresh();
    var written = FormatRegistry.Read(destination);
    Assert.That(written, Is.Not.Null);
    Assert.That((written!.Width, written.Height), Is.EqualTo((6, 3)));
  }

  [Test]
  public void ConvertBatch_OneSourceIsNotAPicture_ReportsItAndKeepsGoing() {
    using var scratch = new ScratchFolder("batch-broken");
    var source = scratch.Sub("in");
    var destination = scratch.Sub("out");

    var good = ViewerTestPictures.WritePng(source, "a-good.png", 5, 5, 1, 2, 3);
    var broken = new FileInfo(Path.Combine(source.FullName, "b-broken.png"));
    File.WriteAllBytes(broken.FullName, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00]);
    var alsoGood = ViewerTestPictures.WritePng(source, "c-good.png", 7, 2, 4, 5, 6);

    var results = ConversionService.ConvertBatch([good, broken, alsoGood], _Bmp(), destination, overwrite: true);

    Assert.That(results, Has.Exactly(3).Items, "the batch stopped at the file it could not read");
    Assert.That(results[0].Succeeded, Is.True, results[0].Message);
    Assert.That(results[1].Succeeded, Is.False);
    Assert.That(results[2].Succeeded, Is.True, results[2].Message);
  }

  [Test]
  public void ConvertBatch_ReportsProgressOncePerFileAndOnceAtTheEnd() {
    using var scratch = new ScratchFolder("batch-progress");
    var source = scratch.Sub("in");
    var destination = scratch.Sub("out");
    var files = new[] {
      ViewerTestPictures.WritePng(source, "a.png", 2, 2, 0, 0, 0),
      ViewerTestPictures.WritePng(source, "b.png", 2, 2, 0, 0, 0),
    };

    var seen = new System.Collections.Generic.List<(int Done, int Total)>();
    ConversionService.ConvertBatch(files, _Bmp(), destination, overwrite: true, progress: (done, total) => seen.Add((done, total)));

    Assert.That(seen, Is.EqualTo(new[] { (0, 2), (1, 2), (2, 2) }));
  }

  [TestCase(0, 1, ".png", "picture.01.png")]
  [TestCase(9, 10, ".png", "picture.10.png")]
  [TestCase(0, 100, ".bmp", "picture.001.bmp")]
  [TestCase(99, 1000, ".bmp", "picture.0100.bmp")]
  public void PageFile_NumbersAPageSoItSortsBackIntoOrder(int page, int pageCount, string extension, string expected) {
    using var scratch = new ScratchFolder("page-name");

    var file = ConversionService.PageFile(scratch.Directory, "picture", page, pageCount, extension);

    Assert.That(file.Name, Is.EqualTo(expected));
  }
}
