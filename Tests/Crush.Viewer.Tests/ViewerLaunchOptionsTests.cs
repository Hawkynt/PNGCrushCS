using System.IO;

namespace Crush.Viewer.Tests;

/// <summary>What the command line is understood to have asked for.</summary>
[TestFixture]
public sealed class ViewerLaunchOptionsTests {

  [Test]
  public void Parse_NoArguments_AsksForNothing() {
    var options = ViewerLaunchOptions.Parse([]);

    Assert.That(options.InitialPath, Is.Null);
    Assert.That(options.ScreenshotPath, Is.Null);
    Assert.That(options.SmokeTest, Is.False);
  }

  [Test]
  public void Parse_ABarePath_IsThePictureToOpen() {
    var options = ViewerLaunchOptions.Parse(["picture.png"]);

    Assert.That(options.InitialPath, Is.EqualTo(Path.GetFullPath("picture.png")));
  }

  [Test]
  public void Parse_APathIsMadeAbsolute() {
    var options = ViewerLaunchOptions.Parse(["--open", Path.Combine("sub", "picture.png")]);

    Assert.That(options.InitialPath, Is.EqualTo(Path.GetFullPath(Path.Combine("sub", "picture.png"))));
    Assert.That(Path.IsPathRooted(options.InitialPath!), Is.True);
  }

  [Test]
  public void Parse_ScreenshotTakesTheFollowingArgument() {
    var options = ViewerLaunchOptions.Parse(["--screenshot", "shot.png"]);

    Assert.That(options.ScreenshotPath, Is.EqualTo(Path.GetFullPath("shot.png")));
    Assert.That(options.InitialPath, Is.Null, "the screenshot path was also taken as a picture to open");
  }

  [Test]
  public void Parse_ScreenshotWithNothingAfterIt_IsNotAPath() {
    // Program turns this into an error and exit code 2; what matters here is that the flag does not
    // quietly become a screenshot to an empty path, or swallow the next flag as one.
    var options = ViewerLaunchOptions.Parse(["--screenshot"]);

    Assert.That(options.ScreenshotPath, Is.Null);
  }

  [Test]
  public void Parse_SmokeTestIsAFlagAndTakesNoArgument() {
    var options = ViewerLaunchOptions.Parse(["--smoke-test", "picture.png"]);

    Assert.That(options.SmokeTest, Is.True);
    Assert.That(options.InitialPath, Is.EqualTo(Path.GetFullPath("picture.png")));
  }

  [Test]
  public void Parse_AnUnknownFlagIsIgnoredAndDoesNotBecomeAPath() {
    var options = ViewerLaunchOptions.Parse(["--no-such-option", "picture.png"]);

    Assert.That(options.InitialPath, Is.EqualTo(Path.GetFullPath("picture.png")));
  }

  [Test]
  public void Parse_TheFirstBarePathWins() {
    var options = ViewerLaunchOptions.Parse(["first.png", "second.png"]);

    Assert.That(options.InitialPath, Is.EqualTo(Path.GetFullPath("first.png")));
  }

  [Test]
  public void Parse_AnExplicitOpenOverridesABarePathAlreadySeen() {
    var options = ViewerLaunchOptions.Parse(["bare.png", "--open", "explicit.png"]);

    Assert.That(options.InitialPath, Is.EqualTo(Path.GetFullPath("explicit.png")));
  }

  [Test]
  public void Parse_AnEmptyPathArgument_IsTreatedAsThoughItWereNotGiven() {
    var options = ViewerLaunchOptions.Parse(["--open", "   ", "--screenshot", ""]);

    Assert.That(options.InitialPath, Is.Null);
    Assert.That(options.ScreenshotPath, Is.Null);
  }

  [Test]
  public void Parse_EverythingAtOnce() {
    var options = ViewerLaunchOptions.Parse(["--smoke-test", "--open", "picture.png", "--screenshot", "shot.png"]);

    Assert.That(options.SmokeTest, Is.True);
    Assert.That(options.InitialPath, Is.EqualTo(Path.GetFullPath("picture.png")));
    Assert.That(options.ScreenshotPath, Is.EqualTo(Path.GetFullPath("shot.png")));
  }
}
