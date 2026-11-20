// Sets the product version and closes the changelog section for it.
//
//     dotnet run tools/bump_version.cs -- X.Y.Z
//
// The version is written in one place, VersionPrefix in src/AiUsage/AiUsage.csproj; the menu and
// the MSIX read it from there. The tool exists so the bump and the changelog header move
// together: forgetting one of the two is how a tag ends up pointing at a changelog that still
// says "Unreleased".
//
//     src/AiUsage/AiUsage.csproj   <VersionPrefix>X.Y.Z</VersionPrefix>
//     CHANGELOG.md                 "## [Unreleased]" becomes "## [X.Y.Z] - YYYY-MM-DD", with a
//                                  fresh empty "## [Unreleased]" above it
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

if (args.Length != 1 || !Regex.IsMatch(args[0], @"^\d+\.\d+\.\d+$"))
{
    Console.Error.WriteLine("usage: dotnet run tools/bump_version.cs -- X.Y.Z");
    return 2;
}

var version = args[0];
var root = Path.GetDirectoryName(Path.GetDirectoryName(Source.Location()))!;
var today = DateTime.Now.ToString("yyyy-MM-dd");
var missed = 0;

Replace("src/AiUsage/AiUsage.csproj", "<VersionPrefix>[^<]*</VersionPrefix>", $"<VersionPrefix>{version}</VersionPrefix>");
Replace("CHANGELOG.md", @"^## \[Unreleased\]$", $"## [Unreleased]\n\n## [{version}] - {today}");

Console.WriteLine($"\nversion {version} written to {2 - missed} files");
return missed == 0 ? 0 : 1;

void Replace(string file, string pattern, string replacement)
{
    var path = Path.Combine(root, file);
    var regex = new Regex(pattern, RegexOptions.Multiline);
    var text = File.ReadAllText(path);
    if (!regex.IsMatch(text))
    {
        Console.WriteLine($"  MISSED   {file}  <- pattern did not match, fix by hand");
        missed++;
        return;
    }
    File.WriteAllText(path, regex.Replace(text, replacement, 1));
    Console.WriteLine($"  updated  {file}");
}

static class Source
{
    /// <summary>This file, as the compiler saw it: the repo root is two folders up.</summary>
    public static string Location([CallerFilePath] string path = "") => path;
}
