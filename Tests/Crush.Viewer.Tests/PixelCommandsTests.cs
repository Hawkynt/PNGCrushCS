using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FileFormat.Core;
using Hawkynt.ImageProcessing;

namespace Crush.Viewer.Tests;

/// <summary>The ribbon's live pixel commands: each changes the picture, and none touches the one it was given.</summary>
/// <remarks>
/// The viewer keeps no undo history. What it does keep is the contract Rotate and Resize already
/// keep: an edit is a new picture, and the picture it was made from is left exactly as it was — which
/// is the whole of what an undo would need, and the property these check for every command. The
/// dialogs, the preview and the background thread are the toolkit's side and are covered by the
/// screenshot run instead.
/// </remarks>
[TestFixture]
public sealed class PixelCommandsTests {

  /// <summary>
  /// A picture every command has something to do to: colour, a tonal range well inside the scale,
  /// soft gradients, hard edges, fine noise and a red disc.
  /// </summary>
  private static RawImage _Picture(int width = 64, int height = 48) {
    var pixels = new byte[width * height * 4];
    var random = new Random(1234);
    for (var y = 0; y < height; ++y)
      for (var x = 0; x < width; ++x) {
        var at = (y * width + x) * 4;
        var noise = random.Next(-6, 7);
        var edge = (x / 8 + y / 8) % 2 == 0 ? 30 : 0;
        pixels[at] = (byte)Math.Clamp(60 + x * 2 + noise + edge, 0, 255);
        pixels[at + 1] = (byte)Math.Clamp(50 + y * 3 + noise, 0, 255);
        pixels[at + 2] = (byte)Math.Clamp(90 + (x + y) + noise - edge, 0, 255);
        pixels[at + 3] = 255;

        var dx = x - 20;
        var dy = y - 20;
        if (dx * dx + dy * dy < 36) {
          pixels[at] = 40;
          pixels[at + 1] = 30;
          pixels[at + 2] = 200;
        }
      }

    return new() { Width = width, Height = height, Format = PixelFormat.Bgra32, PixelData = pixels };
  }

  private static IEnumerable<TestCaseData> _Commands()
    => PixelCommands.All.Select(c => new TestCaseData(c.Text).SetName($"Command_ChangesThePictureAndLeavesTheSourceAlone({c.Text.TrimEnd('…')})"));

  [TestCaseSource(nameof(_Commands))]
  public void Command_ChangesThePictureAndLeavesTheSourceAlone(string text) {
    var command = PixelCommands.Find(text)!;
    var source = _Picture();
    var before = (byte[])source.PixelData.Clone();

    var result = command.RunWithInitialValues(source);

    Assert.That(source.PixelData, Is.EqualTo(before), "the command wrote through the picture it was given");
    Assert.That(source.Width, Is.EqualTo(64));
    var changedSize = result.Width != source.Width || result.Height != source.Height;
    Assert.That(changedSize || !result.ToBgra32().SequenceEqual(before), Is.True, "the command left the picture as it was");
  }

  [Test]
  public void PixelArtCommands_DoubleThePicture() {
    foreach (var command in PixelCommands.PixelArt) {
      var result = command.RunWithInitialValues(_Picture());

      Assert.That((result.Width, result.Height), Is.EqualTo((128, 96)), command.Text);
    }
  }

  [Test]
  public void CommandsWithoutSettings_AreTheOnesThatApplyAtOnce() {
    var immediate = PixelCommands.All.Where(c => !c.AsksFirst).Select(c => c.Text).ToArray();

    Assert.That(immediate, Is.EquivalentTo(new[] { "Scale2x", "Eagle", "xBR", "Auto levels", "Equalize" }));
  }

  [Test]
  public void EveryCommand_SaysWhatItDoes_AndOpensInsideItsOwnRanges() {
    foreach (var command in PixelCommands.All) {
      Assert.That(command.Does, Does.EndWith("."), command.Text);
      foreach (var setting in command.Settings) {
        Assert.That(setting.Minimum, Is.LessThan(setting.Maximum), $"{command.Text}: {setting.Label}");
        Assert.That(setting.Initial, Is.InRange(setting.Minimum, setting.Maximum), $"{command.Text}: {setting.Label}");
      }
    }
  }

  [Test]
  public void CommandTexts_AreUnique_AcrossLiveAndDisabledCommands() {
    var texts = PixelCommands.All.Select(c => c.Text)
      .Concat(PlannedCommands.Colour.Concat(PlannedCommands.Repair).Concat(PlannedCommands.Automatic).Select(c => c.Text));

    Assert.That(texts, Is.Unique);
  }

  [TestCase("Gaussian blur…")]
  [TestCase("Gaussian blur")]
  [TestCase("gaussian BLUR")]
  public void Find_TakesTheTextWithOrWithoutItsEllipsis(string text)
    => Assert.That(PixelCommands.Find(text)?.Text, Is.EqualTo("Gaussian blur…"));

  [Test]
  public void Find_ReturnsNothingForAnUnknownCommand()
    => Assert.That(PixelCommands.Find("Sharpen the cat"), Is.Null);

  [Test]
  public void Regenerate_OffersTheEnginesSixKernels() {
    var kernel = PixelCommands.Regenerate.Settings.Single();

    Assert.That(kernel.Choices, Has.Length.EqualTo(Enum.GetValues<ResampleKernel>().Length));
    Assert.That(PixelCommands.Kernels, Is.EquivalentTo(Enum.GetValues<ResampleKernel>()));
    Assert.That(PixelCommands.Kernels[(int)kernel.Initial], Is.EqualTo(ResampleKernel.Mitchell));
  }

  [Test]
  public void Regenerate_WithAnInterpolatingKernel_LeavesThePictureAlone() {
    var source = _Picture();
    var nearest = Array.IndexOf(PixelCommands.Kernels, ResampleKernel.Nearest);

    var result = PixelCommands.Regenerate.Run(source, [nearest]);

    Assert.That(result.ToBgra32(), Is.EqualTo(source.PixelData));
  }

  // ------------------------------------------------------------------------------------------
  // Curves
  // ------------------------------------------------------------------------------------------

  [Test]
  public void Curve_PassesThroughItsPoints() {
    double[] inputs = [0, 0.25, 0.5, 0.75, 1];
    double[] outputs = [0.1, 0.2, 0.5, 0.85, 0.9];
    var curve = ToneCurves.Through(inputs, outputs);

    for (var i = 0; i < inputs.Length; ++i)
      Assert.That(curve(inputs[i]), Is.EqualTo(outputs[i]).Within(1e-12), $"point {i}");
  }

  [Test]
  public void Curve_ThroughPointsOnALine_IsThatLine() {
    var curve = ToneCurves.Through([0, 0.25, 0.5, 0.75, 1], [0, 0.25, 0.5, 0.75, 1]);

    for (var v = 0.0; v <= 1.0; v += 0.01)
      Assert.That(curve(v), Is.EqualTo(v).Within(1e-12));
  }

  /// <summary>The reason for the monotone spline: rising points must give a curve that never falls.</summary>
  [Test]
  public void Curve_ThroughRisingPoints_NeverFallsOrOvershoots() {
    var curve = ToneCurves.Through([0, 0.25, 0.5, 0.75, 1], [0, 0.02, 0.5, 0.98, 1]);

    var previous = curve(0);
    for (var v = 0.001; v <= 1.0; v += 0.001) {
      var current = curve(v);
      Assert.That(current, Is.GreaterThanOrEqualTo(previous - 1e-12), $"falls at {v}");
      Assert.That(current, Is.InRange(0.0, 1.0), $"overshoots at {v}");
      previous = current;
    }
  }

  [Test]
  public void Curve_IsFlatOutsideItsFirstAndLastPoint() {
    var curve = ToneCurves.Through([0.2, 0.8], [0.3, 0.6]);

    Assert.That(curve(0), Is.EqualTo(0.3));
    Assert.That(curve(1), Is.EqualTo(0.6));
  }

  // ------------------------------------------------------------------------------------------
  // Red-eye inside a marked area
  // ------------------------------------------------------------------------------------------

  [Test]
  public void MarkedArea_OnASquarePixelPicture_IsTheSameRectangle() {
    var area = MarkedArea.ToPicture(10, 12, 20, 8, 64, 48, 64, 48);

    Assert.That(area, Is.EqualTo(new PixelRect(10, 12, 20, 8)));
  }

  [Test]
  public void MarkedArea_OnAWidenedPicture_IsNarrowedBack() {
    // A picture of 64 stored pixels shown 128 wide, the way a double-width video mode is shown.
    var area = MarkedArea.ToPicture(20, 10, 40, 10, 128, 48, 64, 48);

    Assert.That(area, Is.EqualTo(new PixelRect(10, 10, 20, 10)));
  }

  [Test]
  public void MarkedArea_RoundsOutwards_AndIsClippedToThePicture() {
    var area = MarkedArea.ToPicture(-5.5, 40.2, 20, 20, 64, 48, 64, 48);

    Assert.That(area, Is.EqualTo(new PixelRect(0, 40, 15, 8)));
  }

  [Test]
  public void MarkedArea_ThatMissesThePicture_IsEmpty()
    => Assert.That(MarkedArea.ToPicture(100, 100, 5, 5, 64, 48, 64, 48).IsEmpty, Is.True);

  [Test]
  public void RedEye_ChangesTheMarkedEye_AndNothingOutsideTheArea() {
    var source = _Picture();
    var area = new PixelRect(14, 14, 12, 12);

    var result = MarkedArea.ReduceRedEye(source, area, (0, 0), 0.4).ToBgra32();

    var changedInside = false;
    for (var y = 0; y < source.Height; ++y)
      for (var x = 0; x < source.Width; ++x) {
        var at = (y * source.Width + x) * 4;
        var inside = x >= area.X && x < area.Right && y >= area.Y && y < area.Bottom;
        var same = result.AsSpan(at, 4).SequenceEqual(source.PixelData.AsSpan(at, 4));
        if (!inside)
          Assert.That(same, Is.True, $"pixel {x},{y} outside the marked area changed");
        else if (!same)
          changedInside = true;
      }

    Assert.That(changedInside, Is.True, "the red disc inside the area was not touched");
    Assert.That(result[(20 * source.Width + 20) * 4 + 2], Is.LessThan(200), "the middle of the eye is still as red");
  }

  /// <summary>The preview runs on a cut-out; it has to retouch the same pixels the real run will.</summary>
  [Test]
  public void RedEye_OnACutOut_MatchesTheSamePixelsOfTheWholePicture() {
    var source = _Picture();
    var area = new PixelRect(14, 14, 12, 12);
    var around = MarkedArea.WithSurroundings(area, source.Width, source.Height);
    var cutPixels = new byte[around.Width * around.Height * 4];
    for (var y = 0; y < around.Height; ++y)
      source.PixelData.AsSpan(((around.Y + y) * source.Width + around.X) * 4, around.Width * 4)
        .CopyTo(cutPixels.AsSpan(y * around.Width * 4));
    var cut = new RawImage { Width = around.Width, Height = around.Height, Format = PixelFormat.Bgra32, PixelData = cutPixels };

    var whole = MarkedArea.ReduceRedEye(source, area, (0, 0), 0.4).ToBgra32();
    var part = MarkedArea.ReduceRedEye(cut, area, (around.X, around.Y), 0.4).ToBgra32();

    for (var y = 0; y < around.Height; ++y)
      for (var x = 0; x < around.Width; ++x) {
        var fromWhole = whole.AsSpan(((around.Y + y) * source.Width + around.X + x) * 4, 4).ToArray();
        var fromPart = part.AsSpan((y * around.Width + x) * 4, 4).ToArray();
        Assert.That(fromPart, Is.EqualTo(fromWhole), $"pixel {x},{y} of the cut-out");
      }
  }

  // ------------------------------------------------------------------------------------------
  // Speed
  // ------------------------------------------------------------------------------------------

  /// <summary>How long the heaviest everyday command takes on a twelve-megapixel photograph.</summary>
  /// <remarks>
  /// Advisory: the command runs off the interface thread, so this is how long the status bar says
  /// "working", not how long the window is frozen. It is here to catch an order-of-magnitude slip.
  /// </remarks>
  [Test]
  [Category("Performance")]
  public void GaussianBlur_OnTwelveMegapixels_FinishesInSeconds() {
    var picture = new RawImage {
      Width = 4000,
      Height = 3000,
      Format = PixelFormat.Rgb24,
      PixelData = new byte[4000 * 3000 * 3],
    };
    new Random(7).NextBytes(picture.PixelData);
    var command = PixelCommands.Find("Gaussian blur")!;

    var clock = Stopwatch.StartNew();
    var result = command.RunWithInitialValues(picture);
    clock.Stop();

    TestContext.Out.WriteLine($"Gaussian blur, sigma {command.Settings[0].Initial}, 4000 x 3000: {clock.Elapsed.TotalSeconds:0.00} s");
    Assert.That(result.Width, Is.EqualTo(4000));
    Assert.That(clock.Elapsed, Is.LessThan(TimeSpan.FromSeconds(30)));
  }
}
