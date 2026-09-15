using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CommandLine;
using Crush.Image.Verbs;

namespace Crush.Image.Tests;

/// <summary>
/// Parsing tests for the crush CLI verbs, aimed at the on-by-default flags.
/// </summary>
/// <remarks>
/// CommandLineParser treats a plain <see langword="bool"/> option as a switch: present means true,
/// absent means the declared default, and any value written after the flag is consumed and thrown
/// away. An option declared <c>bool</c> with <c>Default = true</c> is therefore permanently on —
/// it advertises <c>(Default: true)</c> in the help screen while offering no way to say no.
/// Declaring the property <c>bool?</c> makes the parser accept an explicit value instead.
/// These fixtures pin down both halves of that: every on-by-default flag can be declined, and the
/// help screen still reads exactly as it did.
/// </remarks>
[TestFixture]
public sealed class VerbOptionTests {

  /// <summary>One on-by-default flag: where it lives, how it is written, what the help screen says.</summary>
  public sealed record OnByDefaultFlag(string Verb, Type VerbType, string SwitchSpec, string LongName, string Property, string HelpText) {
    public override string ToString() => $"{this.Verb} --{this.LongName}";
  }

  /// <summary>
  /// Every flag that is on unless the user says otherwise. Spelled out rather than reflected so
  /// that dropping one, renaming one or rewording its help text has to be a deliberate edit here.
  /// <see cref="TheDeclaredOnByDefaultFlags_AreExactlyThisList"/> keeps it honest against the verbs.
  /// </summary>
  private static readonly OnByDefaultFlag[] _OnByDefaultFlags = [
    new("auto", typeof(AutoVerb), "--convert", "convert", nameof(AutoVerb.AllowConversion),
      "Try converting to other formats for smaller output"),
    new("auto", typeof(AutoVerb), "--auto-extension", "auto-extension", nameof(AutoVerb.AutoExtension),
      "Automatically change output extension to match best format"),

    new("png", typeof(PngVerb), "-a, --auto-color-mode", "auto-color-mode", nameof(PngVerb.AutoColorMode),
      "Automatically select best color mode"),
    new("png", typeof(PngVerb), "--interlace", "interlace", nameof(PngVerb.TryInterlacing),
      "Try interlaced PNG encoding"),
    new("png", typeof(PngVerb), "-p, --partition", "partition", nameof(PngVerb.TryPartitioning),
      "Try smart partitioning for better compression"),

    new("gif", typeof(GifVerb), "--optimize-disposal", "optimize-disposal", nameof(GifVerb.OptimizeDisposal),
      "Optimize frame disposal methods"),
    new("gif", typeof(GifVerb), "--trim-margins", "trim-margins", nameof(GifVerb.TrimMargins),
      "Trim transparent margins from frames"),
    new("gif", typeof(GifVerb), "--deferred-clear", "deferred-clear", nameof(GifVerb.DeferredClear),
      "Try deferred LZW clear codes; pass 'false' to skip (--deferred-clear false)"),
    new("gif", typeof(GifVerb), "--frozen-dictionary", "frozen-dictionary", nameof(GifVerb.FrozenDictionary),
      "Try a frozen LZW dictionary; pass 'false' to skip (--frozen-dictionary false)"),
    new("gif", typeof(GifVerb), "--frame-diff", "frame-diff", nameof(GifVerb.FrameDiff),
      "Try frame differencing"),
    new("gif", typeof(GifVerb), "--deduplicate", "deduplicate", nameof(GifVerb.Deduplicate),
      "Merge identical consecutive frames"),

    new("tiff", typeof(TiffVerb), "-a, --auto-color-mode", "auto-color-mode", nameof(TiffVerb.AutoColorMode),
      "Automatically select best color mode"),
    new("tiff", typeof(TiffVerb), "--dynamic-strips", "dynamic-strips", nameof(TiffVerb.DynamicStripSizing),
      "Dynamically generate strip sizes"),

    new("bmp", typeof(BmpVerb), "-a, --auto-color-mode", "auto-color-mode", nameof(BmpVerb.AutoColorMode),
      "Automatically select best color mode"),

    new("tga", typeof(TgaVerb), "-a, --auto-color-mode", "auto-color-mode", nameof(TgaVerb.AutoColorMode),
      "Automatically select best color mode"),

    new("pcx", typeof(PcxVerb), "-a, --auto-color-mode", "auto-color-mode", nameof(PcxVerb.AutoColorMode),
      "Automatically select best color mode"),

    new("jpeg", typeof(JpegVerb), "--strip", "strip", nameof(JpegVerb.StripMetadata),
      "Strip metadata (EXIF, ICC, comments)"),

    new("webp", typeof(WebPVerb), "-s, --strip-metadata", "strip-metadata", nameof(WebPVerb.StripMetadata),
      "Strip metadata (EXIF, ICCP, XMP)"),
  ];

  private static readonly Type[] _VerbTypes = typeof(AutoVerb).Assembly
    .GetTypes()
    .Where(t => t.GetCustomAttribute<VerbAttribute>() != null)
    .OrderBy(t => t.Name, StringComparer.Ordinal)
    .ToArray();

  private static IEnumerable<OnByDefaultFlag> _OnByDefault() => _OnByDefaultFlags;

  private static IEnumerable<TestCaseData> _OffByDefaultSwitches() {
    foreach (var verbType in _VerbTypes) {
      var verb = verbType.GetCustomAttribute<VerbAttribute>()!.Name;
      foreach (var (property, option) in _OptionsOf(verbType)) {
        if (option.Default is not false)
          continue;
        if (property.PropertyType != typeof(bool) && property.PropertyType != typeof(bool?))
          continue;

        yield return new TestCaseData(verbType, verb, option.LongName, property.Name)
          .SetArgDisplayNames(verb, "--" + option.LongName);
      }
    }
  }

  private static IEnumerable<(PropertyInfo Property, OptionAttribute Option)> _OptionsOf(Type verbType) =>
    verbType
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Select(p => (Property: p, Option: p.GetCustomAttribute<OptionAttribute>()!))
      .Where(x => x.Option != null);

  /// <summary>Parses one verb, supplying whatever that verb declares as required.</summary>
  /// <remarks>Goes through the same preprocessing <c>Program.Main</c> applies, so what is under
  /// test here is the command line as the shipped CLI sees it.</remarks>
  private static object _Parse(Type verbType, string verb, params string[] extra) =>
    _ParseExactly([verb, .. _RequiredOptionsOf(verbType, verb), .. extra]);

  /// <summary>As <see cref="_Parse"/>, but with the extra tokens ahead of the required options.</summary>
  private static object _ParseWithFlagFirst(Type verbType, string verb, params string[] extra) =>
    _ParseExactly([verb, .. extra, .. _RequiredOptionsOf(verbType, verb)]);

  private static IEnumerable<string> _RequiredOptionsOf(Type verbType, string verb) {
    foreach (var (property, option) in _OptionsOf(verbType)) {
      if (!option.Required)
        continue;

      Assert.That(property.PropertyType, Is.EqualTo(typeof(string)),
        $"{verb} --{option.LongName} is required but not a string; this helper only knows how to fill strings in.");
      yield return "--" + option.LongName;
      yield return "placeholder";
    }
  }

  private static object _ParseExactly(string[] args) {
    using var diagnostics = new StringWriter();
    var parser = new Parser(settings => settings.HelpWriter = diagnostics);
    var result = parser.ParseArguments(VerbArguments.SupplyImplicitTrue(args), _VerbTypes);
    if (result is not Parsed<object> parsed)
      Assert.Fail($"'{string.Join(" ", args)}' did not parse:{Environment.NewLine}{diagnostics}");

    return ((Parsed<object>)result).Value;
  }

  private static object? _ValueOf(object parsedVerb, string property) =>
    parsedVerb.GetType().GetProperty(property)!.GetValue(parsedVerb);

  /// <summary>The whole help screen for one verb, as the user sees it.</summary>
  private static string _HelpFor(string verb) {
    using var help = new StringWriter();
    var parser = new Parser(settings => settings.HelpWriter = help);
    parser.ParseArguments([verb, "--help"], _VerbTypes);
    return help.ToString();
  }

  /// <summary>
  /// The block the help screen devotes to one option, with its line wrapping flattened away —
  /// the description column is as wide as the longest switch in that verb, so the wrap points
  /// differ per verb and are not what these tests are about.
  /// </summary>
  private static string _HelpEntryFor(string help, string longName) {
    var lines = help.Replace("\r\n", "\n").Split('\n');
    var start = Array.FindIndex(lines, l => Regex.IsMatch(l, @"^\s\s(-\w, )?--" + Regex.Escape(longName) + @"(\s|$)"));
    Assert.That(start, Is.GreaterThanOrEqualTo(0), $"--{longName} does not appear in the help screen:{Environment.NewLine}{help}");

    var end = start + 1;
    while (end < lines.Length && !Regex.IsMatch(lines[end], @"^\s\s-"))
      ++end;

    return Regex.Replace(string.Join(" ", lines[start..end]), @"\s+", " ").Trim();
  }

  [Test]
  [Category("Unit")]
  [TestCaseSource(nameof(_OnByDefault))]
  public void OnByDefaultFlag_Absent_IsOn(OnByDefaultFlag flag) {
    var parsed = _Parse(flag.VerbType, flag.Verb);

    Assert.That(_ValueOf(parsed, flag.Property), Is.True);
  }

  [Test]
  [Category("Unit")]
  [TestCaseSource(nameof(_OnByDefault))]
  public void OnByDefaultFlag_Bare_IsOn(OnByDefaultFlag flag) {
    var parsed = _Parse(flag.VerbType, flag.Verb, "--" + flag.LongName);

    Assert.That(_ValueOf(parsed, flag.Property), Is.True);
  }

  /// <summary>
  /// A bare flag is not always the last word on the command line. A scalar option that finds
  /// another option where its value should be is rejected outright, which is what
  /// <see cref="VerbArguments.SupplyImplicitTrue"/> exists to prevent.
  /// </summary>
  [Test]
  [Category("Unit")]
  [TestCaseSource(nameof(_OnByDefault))]
  public void OnByDefaultFlag_BareBeforeAnotherOption_IsOn(OnByDefaultFlag flag) {
    var parsed = _Parse(flag.VerbType, flag.Verb, "--" + flag.LongName, "--jobs", "2");

    Assert.That(_ValueOf(parsed, flag.Property), Is.True);
  }

  [Test]
  [Category("Unit")]
  [TestCaseSource(nameof(_OnByDefault))]
  public void OnByDefaultFlag_BareAheadOfTheRequiredOptions_IsOn(OnByDefaultFlag flag) {
    var parsed = _ParseWithFlagFirst(flag.VerbType, flag.Verb, "--" + flag.LongName);

    Assert.That(_ValueOf(parsed, flag.Property), Is.True);
  }

  /// <summary>Two bare on-by-default flags in a row are two scalar options in a row.</summary>
  [Test]
  [Category("Unit")]
  [TestCaseSource(nameof(_OnByDefault))]
  public void OnByDefaultFlag_BareBesideEveryOtherOnByDefaultFlagOfItsVerb_IsOn(OnByDefaultFlag flag) {
    var siblings = _OnByDefaultFlags
      .Where(f => f.VerbType == flag.VerbType && f.LongName != flag.LongName)
      .Select(f => "--" + f.LongName);

    var parsed = _Parse(flag.VerbType, flag.Verb, ["--" + flag.LongName, .. siblings]);

    Assert.That(_ValueOf(parsed, flag.Property), Is.True);
  }

  [Test]
  [Category("Unit")]
  [TestCaseSource(nameof(_OnByDefault))]
  public void OnByDefaultFlag_ExplicitTrue_IsOn(OnByDefaultFlag flag) {
    var parsed = _Parse(flag.VerbType, flag.Verb, "--" + flag.LongName, "true");

    Assert.That(_ValueOf(parsed, flag.Property), Is.True);
  }

  /// <summary>The one the whole fixture exists for: an on-by-default flag has to be declinable.</summary>
  [Test]
  [Category("Unit")]
  [TestCaseSource(nameof(_OnByDefault))]
  public void OnByDefaultFlag_ExplicitFalse_IsOff(OnByDefaultFlag flag) {
    var parsed = _Parse(flag.VerbType, flag.Verb, "--" + flag.LongName, "false");

    Assert.That(_ValueOf(parsed, flag.Property), Is.False);
  }

  [Test]
  [Category("Unit")]
  [TestCaseSource(nameof(_OnByDefault))]
  public void OnByDefaultFlag_ExplicitFalseWithEqualsSign_IsOff(OnByDefaultFlag flag) {
    var parsed = _Parse(flag.VerbType, flag.Verb, $"--{flag.LongName}=false");

    Assert.That(_ValueOf(parsed, flag.Property), Is.False);
  }

  /// <summary>
  /// Accepting a value is only half of it: the property has to be nullable for the parser to read
  /// one, and a future flag declared plain <c>bool</c> would be silently inert all over again.
  /// </summary>
  [Test]
  [Category("Unit")]
  [TestCaseSource(nameof(_OnByDefault))]
  public void OnByDefaultFlag_IsDeclaredNullable(OnByDefaultFlag flag) {
    var property = flag.VerbType.GetProperty(flag.Property);

    Assert.That(property, Is.Not.Null);
    Assert.That(property!.PropertyType, Is.EqualTo(typeof(bool?)),
      $"{flag.VerbType.Name}.{flag.Property} is declared {property.PropertyType.Name}. CommandLineParser "
      + "reads an explicit value only for a nullable bool, so as a plain bool this flag cannot be turned off.");
  }

  /// <summary>The fix must not cost the user the <c>(Default: true)</c> the help screen has always shown.</summary>
  [Test]
  [Category("Unit")]
  [TestCaseSource(nameof(_OnByDefault))]
  public void OnByDefaultFlag_HelpScreenEntry_IsUnchanged(OnByDefaultFlag flag) {
    var entry = _HelpEntryFor(_HelpFor(flag.Verb), flag.LongName);

    Assert.That(entry, Is.EqualTo($"{flag.SwitchSpec} (Default: true) {flag.HelpText}"));
  }

  /// <summary>
  /// An off-by-default flag works correctly as a switch and is deliberately left as a plain
  /// <c>bool</c>: absent is off, present is on, and there is nothing to decline.
  /// </summary>
  [Test]
  [Category("Unit")]
  [TestCaseSource(nameof(_OffByDefaultSwitches))]
  public void OffByDefaultSwitch_BehavesAsASwitch(Type verbType, string verb, string longName, string property) {
    Assert.Multiple(() => {
      Assert.That(_ValueOf(_Parse(verbType, verb), property), Is.False, $"{verb} --{longName} absent");
      Assert.That(_ValueOf(_Parse(verbType, verb, "--" + longName), property), Is.True, $"{verb} --{longName} present");
      Assert.That(verbType.GetProperty(property)!.PropertyType, Is.EqualTo(typeof(bool)),
        $"{verb} --{longName} does not need to be nullable to work.");
    });
  }

  /// <summary>
  /// Guards the hand-written table above against the verbs drifting away from it — a new
  /// on-by-default flag has to be added here, which is where the rest of the fixture picks it up.
  /// </summary>
  [Test]
  [Category("Unit")]
  public void TheDeclaredOnByDefaultFlags_AreExactlyThisList() {
    var declared = _VerbTypes
      .SelectMany(verbType => _OptionsOf(verbType)
        .Where(x => x.Option.Default is true)
        .Select(x => $"{verbType.GetCustomAttribute<VerbAttribute>()!.Name} --{x.Option.LongName}"))
      .OrderBy(x => x, StringComparer.Ordinal)
      .ToArray();

    var covered = _OnByDefaultFlags
      .Select(f => $"{f.Verb} --{f.LongName}")
      .OrderBy(x => x, StringComparer.Ordinal)
      .ToArray();

    Assert.That(declared, Is.EqualTo(covered));
  }

  /// <summary>Every option on every verb is one of the two shapes the fixtures above cover.</summary>
  [Test]
  [Category("Unit")]
  public void EveryBooleanOption_DeclaresABooleanDefault() {
    var untyped = _VerbTypes
      .SelectMany(verbType => _OptionsOf(verbType)
        .Where(x => x.Property.PropertyType == typeof(bool) || x.Property.PropertyType == typeof(bool?))
        .Where(x => x.Option.Default is not bool)
        .Select(x => $"{verbType.GetCustomAttribute<VerbAttribute>()!.Name} --{x.Option.LongName}"))
      .ToArray();

    Assert.That(untyped, Is.Empty, "a boolean option without a declared Default is one nobody can reason about");
  }
}
