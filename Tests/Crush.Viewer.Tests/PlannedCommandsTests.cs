using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Crush.Viewer.Tests;

/// <summary>The ribbon's commands that are placed but that neither image library can serve.</summary>
[TestFixture]
public sealed class PlannedCommandsTests {

  private static IReadOnlyList<PlannedCommand> _All()
    => typeof(PlannedCommands)
      .GetProperties(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
      .Where(p => p.PropertyType == typeof(PlannedCommand[]))
      .SelectMany(p => (PlannedCommand[])p.GetValue(null)!)
      .ToArray();

  /// <summary>Every icon the viewer embeds, keyed the way its icon set keys them: by file stem.</summary>
  /// <remarks>
  /// Read from the source tree rather than from the viewer's assembly, which this test project
  /// compiles pieces of rather than references. The project embeds every PNG under Icons/*/, so the
  /// folder is the list.
  /// </remarks>
  private static IReadOnlySet<string> _EmbeddedIcons() {
    var folder = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
    while (folder != null && !Directory.Exists(Path.Combine(folder.FullName, "Crush.Viewer", "Icons")))
      folder = folder.Parent;

    Assert.That(folder, Is.Not.Null, "the viewer's icon folder was not found above the test directory");
    return Directory.GetFiles(Path.Combine(folder!.FullName, "Crush.Viewer", "Icons"), "*.png", SearchOption.AllDirectories)
      .Select(Path.GetFileNameWithoutExtension)
      .ToHashSet(StringComparer.Ordinal)!;
  }

  /// <summary>Every command on the ribbon that names an icon, live or not, with its text.</summary>
  private static IEnumerable<(string Text, string Icon)> _AllIcons()
    => _All().Where(c => c.Icon != null).Select(c => (c.Text, c.Icon!))
      .Concat(PixelCommands.All.Where(c => c.Icon != null).Select(c => (c.Text, c.Icon!)));

  [Test]
  public void EveryCommand_SaysWhatItWouldDoAndWhyItIsOff() {
    var all = _All();

    Assert.That(all, Is.Not.Empty);
    foreach (var command in all) {
      Assert.That(command.Text, Is.Not.Empty);
      Assert.That(command.Does, Does.EndWith("."), command.Text);
      Assert.That(command.Why, Does.EndWith("."), command.Text);
      Assert.That(command.ToolTipText, Is.EqualTo($"{command.Does} {command.Why}"), command.Text);
    }
  }

  /// <summary>
  /// A reason that only says "not yet" is the one this table used to give for everything, and it
  /// stopped being true for most of it; every reason now has to name what is missing.
  /// </summary>
  [Test]
  public void NoReason_IsAPlainNotYet() {
    foreach (var command in _All())
      Assert.That(command.Why, Does.Not.Contain("not in the image library yet"), command.Text);
  }

  [Test]
  public void NoDisabledCommand_IsAlsoALiveOne() {
    foreach (var command in _All())
      Assert.That(PixelCommands.Find(command.Text), Is.Null, command.Text);
  }

  [Test]
  public void EveryNamedIcon_IsEmbeddedInTheViewer() {
    var embedded = _EmbeddedIcons();

    foreach (var (text, icon) in _AllIcons())
      Assert.That(embedded, Does.Contain(icon), text);
  }

  [Test]
  public void NoTwoCommands_ShareAnIcon() {
    var shared = _AllIcons()
      .GroupBy(c => c.Icon)
      .Where(g => g.Count() > 1)
      .Select(g => $"{g.Key}: {string.Join(", ", g.Select(c => c.Text))}");

    Assert.That(shared, Is.Empty);
  }

  [Test]
  public void CommandTexts_AreUnique() {
    var texts = _All().Select(c => c.Text).ToArray();

    Assert.That(texts, Is.Unique);
  }
}
