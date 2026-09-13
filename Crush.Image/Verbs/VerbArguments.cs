using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CommandLine;

namespace Crush.Image.Verbs;

/// <summary>Argument rewriting that happens before CommandLineParser is handed the command line.</summary>
public static class VerbArguments {

  /// <summary>Writes an explicit <c>true</c> after every on-by-default flag that was given bare.</summary>
  /// <param name="args">The raw command line.</param>
  /// <returns>The command line the parser should see.</returns>
  /// <remarks>
  /// The on-by-default flags are declared <c>bool?</c> so that <c>--flag false</c> can turn them off.
  /// That makes each of them a scalar option rather than a switch, and a scalar option wants a value:
  /// CommandLineParser rejects a bare <c>--flag</c> outright whenever the next token is another
  /// option, so <c>crush png -a -i in.png -o out.png</c> would stop working. Supplying the value the
  /// user left out keeps the bare spelling valid wherever it appears, and leaves every other token —
  /// <c>--flag false</c>, <c>--flag=false</c>, anything after a <c>--</c> — exactly as it was typed.
  /// </remarks>
  public static string[] SupplyImplicitTrue(IEnumerable<string> args) {
    var tokens = args as IList<string> ?? args.ToList();
    if (tokens.Count < 1)
      return [.. tokens];

    var verbTypes = typeof(VerbArguments).Assembly
      .GetTypes()
      .Where(t => t.GetCustomAttribute<VerbAttribute>() != null)
      .ToArray();

    var verb = _VerbFor(tokens[0], verbTypes);
    if (verb == null)
      return [.. tokens];

    var triState = _TriStateFlagsOf(verb);
    if (triState.Count < 1)
      return [.. tokens];

    var rewritten = new List<string>(tokens.Count + triState.Count);
    for (var i = 0; i < tokens.Count; ++i) {
      var token = tokens[i];
      rewritten.Add(token);

      // Everything past a bare `--` is the user's data, not ours to touch.
      if (token == "--") {
        for (var rest = i + 1; rest < tokens.Count; ++rest)
          rewritten.Add(tokens[rest]);

        break;
      }

      if (!triState.Contains(token))
        continue;

      // A value that is already there stays; only a flag at the end of the line or one butted up
      // against the next option is missing one.
      var next = i + 1 < tokens.Count ? tokens[i + 1] : null;
      if (next == null || next.StartsWith('-'))
        rewritten.Add(bool.TrueString);
    }

    return [.. rewritten];
  }

  /// <summary>The verb the command line selects, which is the default verb when it names none.</summary>
  private static Type? _VerbFor(string firstToken, IReadOnlyCollection<Type> verbTypes) {
    foreach (var verbType in verbTypes) {
      var attribute = verbType.GetCustomAttribute<VerbAttribute>()!;
      if (attribute.Name == firstToken || attribute.Aliases?.Contains(firstToken, StringComparer.Ordinal) == true)
        return verbType;
    }

    return verbTypes.FirstOrDefault(t => t.GetCustomAttribute<VerbAttribute>()!.IsDefault);
  }

  /// <summary>Every spelling — long and short — of the <c>bool?</c> options one verb declares.</summary>
  private static HashSet<string> _TriStateFlagsOf(Type verbType) {
    var result = new HashSet<string>(StringComparer.Ordinal);
    foreach (var property in verbType.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
      var option = property.GetCustomAttribute<OptionAttribute>();
      if (option == null || property.PropertyType != typeof(bool?))
        continue;

      if (!string.IsNullOrEmpty(option.LongName))
        result.Add("--" + option.LongName);

      if (!string.IsNullOrEmpty(option.ShortName))
        result.Add("-" + option.ShortName);
    }

    return result;
  }
}
