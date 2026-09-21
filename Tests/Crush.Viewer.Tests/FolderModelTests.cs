using System.IO;
using System.Linq;

namespace Crush.Viewer.Tests;

/// <summary>Which files a folder offers, in what order, and where walking stops.</summary>
[TestFixture]
public sealed class FolderModelTests {

  [Test]
  public void Open_ListsOnlyWhatARegisteredReaderClaims() {
    using var scratch = new ScratchFolder("listing");
    ViewerTestPictures.WritePng(scratch.Directory, "picture.png", 2, 2, 0, 0, 0);
    File.WriteAllText(Path.Combine(scratch.Directory.FullName, "notes.txt"), "not a picture");
    File.WriteAllText(Path.Combine(scratch.Directory.FullName, "archive.zip"), "nor this");

    var model = new FolderModel();
    model.Open(scratch.Directory);

    Assert.That(model.Files.Select(f => f.Name), Is.EqualTo(new[] { "picture.png" }));
  }

  [Test]
  public void Open_SortsDigitRunsAsNumbers() {
    using var scratch = new ScratchFolder("natural-order");
    foreach (var name in new[] { "shot10.png", "shot9.png", "shot1.png", "shot2.png" })
      ViewerTestPictures.WritePng(scratch.Directory, name, 2, 2, 0, 0, 0);

    var model = new FolderModel();
    model.Open(scratch.Directory);

    Assert.That(
      model.Files.Select(f => f.Name),
      Is.EqualTo(new[] { "shot1.png", "shot2.png", "shot9.png", "shot10.png" }));
  }

  [Test]
  public void Open_SelectsTheNamedFile() {
    using var scratch = new ScratchFolder("select");
    ViewerTestPictures.WritePng(scratch.Directory, "a.png", 2, 2, 0, 0, 0);
    var wanted = ViewerTestPictures.WritePng(scratch.Directory, "b.png", 2, 2, 0, 0, 0);

    var model = new FolderModel();
    model.Open(scratch.Directory, wanted);

    Assert.That(model.Index, Is.EqualTo(1));
    Assert.That(model.Current!.Name, Is.EqualTo("b.png"));
  }

  [Test]
  public void Refresh_KeepsTheSelectionWhenTheFileIsStillThere() {
    using var scratch = new ScratchFolder("refresh");
    ViewerTestPictures.WritePng(scratch.Directory, "b.png", 2, 2, 0, 0, 0);
    var model = new FolderModel();
    model.Open(scratch.Directory, new FileInfo(Path.Combine(scratch.Directory.FullName, "b.png")));

    // A file that sorts before the selected one appears, so keeping the index would be wrong.
    ViewerTestPictures.WritePng(scratch.Directory, "a.png", 2, 2, 0, 0, 0);
    model.Refresh();

    Assert.That(model.Files, Has.Exactly(2).Items);
    Assert.That(model.Current!.Name, Is.EqualTo("b.png"));
    Assert.That(model.Index, Is.EqualTo(1));
  }

  [Test]
  public void Step_StoppingAtTheEnds_RefusesToWalkPastThem() {
    var model = _Three(out var scratch);
    using (scratch) {
      model.WalkMode = WalkMode.Stop;

      model.SelectIndex(0);
      Assert.That(model.Step(-1), Is.EqualTo(-1));
      Assert.That(model.Step(1), Is.EqualTo(1));

      model.SelectIndex(2);
      Assert.That(model.Step(1), Is.EqualTo(-1));
      Assert.That(model.Step(-1), Is.EqualTo(1));
    }
  }

  [Test]
  public void Step_Wrapping_ContinuesFromTheOtherEnd() {
    var model = _Three(out var scratch);
    using (scratch) {
      model.WalkMode = WalkMode.Wrap;

      model.SelectIndex(0);
      Assert.That(model.Step(-1), Is.EqualTo(2));

      model.SelectIndex(2);
      Assert.That(model.Step(1), Is.EqualTo(0));
    }
  }

  [Test]
  public void Step_WithNothingSelected_StartsAtWhicheverEndTheWalkComesFrom() {
    var model = _Three(out var scratch);
    using (scratch) {
      model.Select(null);

      Assert.That(model.Step(1), Is.EqualTo(0));
      Assert.That(model.Step(-1), Is.EqualTo(2));
    }
  }

  [Test]
  public void Step_OnAnEmptyFolder_HasNowhereToGo() {
    using var scratch = new ScratchFolder("empty");
    var model = new FolderModel();
    model.Open(scratch.Directory);

    Assert.That(model.Step(1), Is.EqualTo(-1));
    Assert.That(model.Step(-1), Is.EqualTo(-1));
    Assert.That(model.Current, Is.Null);
  }

  [Test]
  public void Open_AFolderThatIsNotThere_ListsNothingRatherThanThrowing() {
    using var scratch = new ScratchFolder("missing");
    var model = new FolderModel();

    model.Open(new DirectoryInfo(Path.Combine(scratch.Directory.FullName, "gone")));

    Assert.That(model.Files, Is.Empty);
    Assert.That(model.Index, Is.EqualTo(-1));
  }

  [TestCase("a.png", "b.png", -1)]
  [TestCase("shot2.png", "shot10.png", -1)]
  [TestCase("shot10.png", "shot2.png", 1)]
  [TestCase("SHOT2.png", "shot2.png", 0)]
  [TestCase("shot.png", "shot.png", 0)]
  public void NaturalStringComparer_OrdersNamesTheWayAFileManagerDoes(string left, string right, int expected) {
    var comparison = NaturalStringComparer.Instance.Compare(left, right);

    Assert.That(System.Math.Sign(comparison), Is.EqualTo(expected));
  }

  private static FolderModel _Three(out ScratchFolder scratch) {
    scratch = new("three");
    foreach (var name in new[] { "1.png", "2.png", "3.png" })
      ViewerTestPictures.WritePng(scratch.Directory, name, 2, 2, 0, 0, 0);

    var model = new FolderModel();
    model.Open(scratch.Directory);
    return model;
  }
}
