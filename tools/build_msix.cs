// Builds the widget as a Windows app package (MSIX).
//
//     dotnet run tools/build_msix.cs                                       # publish + msix into dist/
//     dotnet run tools/build_msix.cs -- --skip-publish                     # repack the last publish
//     dotnet run tools/build_msix.cs -- --sign build/dev.pfx --password dev   # sideload test; the Store signs itself
//
// Steps: dotnet publish, self-contained since the .NET desktop runtime has no framework package ->
// stage folder with AppxManifest.xml and Assets/ -> makeappx pack. makeappx comes from an installed
// Windows SDK or, failing that, from the Microsoft.Windows.SDK.BuildTools NuGet package downloaded
// once into build/sdk/. Identity lives in tools/msix/package.json, the version in the csproj.
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text.Json;
using System.Text.RegularExpressions;

const string SdkPackage = "Microsoft.Windows.SDK.BuildTools";
const string SdkVersion = "10.0.26100.1742";

var root = Path.GetDirectoryName(Path.GetDirectoryName(Source.Location()))!;
var msix = Path.Combine(root, "tools", "msix");
var project = Path.Combine(root, "src", "AiUsage", "AiUsage.csproj");
var build = Path.Combine(root, "build");
var publish = Path.Combine(build, "publish");
var stage = Path.Combine(build, "stage");

var options = args.ToList();
var skipPublish = options.Remove("--skip-publish");
var pfx = Option("--sign");
var password = Option("--password");
if (options.Count > 0)
    Fail($"unknown arguments: {string.Join(' ', options)}");

using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(msix, "package.json")));
var meta = json.RootElement.EnumerateObject().Where(p => !p.Name.StartsWith('_'))
    .ToDictionary(p => p.Name, p => p.Value.GetString() ?? "");
var version = ProjectVersion();

if (!skipPublish)
    Run("dotnet", "publish", project, "-c", "Release", "-r", $"win-{meta["arch"]}", "--self-contained",
        "-p:VersionSuffix=", "-o", publish);
if (!File.Exists(Path.Combine(publish, meta["executable"])))
    Fail($"{Path.Combine(publish, meta["executable"])} missing");

if (Directory.Exists(stage))
    Directory.Delete(stage, true);
CopyTree(publish, stage);
CopyTree(Path.Combine(msix, "Assets"), Path.Combine(stage, "Assets"));
// MSIX wants four parts, the last reserved for the Store.
var manifest = File.ReadAllText(Path.Combine(msix, "AppxManifest.xml")).Replace("{version}", $"{version}.0");
foreach (var (key, value) in meta)
    manifest = manifest.Replace($"{{{key}}}", SecurityElement.Escape(value));
if (Regex.Match(manifest, @"\{[a-z_]+\}") is { Success: true } left)
    Fail($"AppxManifest.xml: {left.Value} has no value in package.json");
File.WriteAllText(Path.Combine(stage, "AppxManifest.xml"), manifest);

var output = Path.Combine(root, "dist", $"al-data-ai-usage-{version}-{meta["arch"]}.msix");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.Delete(output);
Run(await Tool("makeappx"), "pack", "/o", "/d", stage, "/p", output);
if (pfx is not null)
{
    var sign = new List<string> { "sign", "/fd", "SHA256", "/f", pfx };
    if (password is not null)
        sign.AddRange(["/p", password]);
    Run(await Tool("signtool"), [.. sign, output]);
}
Console.WriteLine($"\n{output}  ({new FileInfo(output).Length / 1_000_000} MB)");

string? Option(string name)
{
    var at = options.IndexOf(name);
    if (at < 0)
        return null;
    if (at + 1 >= options.Count)
        Fail($"{name} needs a value");
    var value = options[at + 1];
    options.RemoveRange(at, 2);
    return value;
}

string ProjectVersion()
{
    var match = Regex.Match(File.ReadAllText(project), @"<VersionPrefix>(\d+\.\d+\.\d+)</VersionPrefix>");
    if (!match.Success)
        Fail($"VersionPrefix not found in {project}");
    return match.Groups[1].Value;
}

static void CopyTree(string from, string to)
{
    foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
    Directory.CreateDirectory(to);
    foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
}

async Task<string> Tool(string name)
{
    var exe = $"{name}.exe";
    foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        if (File.Exists(Path.Combine(dir, exe)))
            return Path.Combine(dir, exe);
    if (Newest(@"C:\Program Files (x86)\Windows Kits\10\bin", exe) is { } installed)
        return installed;
    var sdk = Path.Combine(build, "sdk");
    if (Newest(Path.Combine(sdk, "bin"), exe) is { } cached)
        return cached;
    Console.WriteLine($"{name} not found; downloading {SdkPackage} {SdkVersion}");
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    var data = await http.GetByteArrayAsync($"https://www.nuget.org/api/v2/package/{SdkPackage}/{SdkVersion}");
    using var archive = new ZipArchive(new MemoryStream(data));
    // the whole x64 folder: makeappx loads its dlls through side-by-side manifests
    foreach (var entry in archive.Entries.Where(e => e.Name.Length > 0 && Regex.IsMatch(e.FullName, "^bin/[^/]+/x64/", RegexOptions.IgnoreCase)))
    {
        var target = Path.Combine(sdk, entry.FullName);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        entry.ExtractToFile(target, true);
    }
    return Newest(Path.Combine(sdk, "bin"), exe) ?? Fail($"{exe} not found, install the Windows SDK");
}

static string? Newest(string bin, string exe) =>
    Directory.Exists(bin)
        ? Directory.EnumerateDirectories(bin).Select(dir => Path.Combine(dir, "x64", exe)).Where(File.Exists)
            .Order(StringComparer.OrdinalIgnoreCase).LastOrDefault()
        : null;

static void Run(string file, params string[] arguments)
{
    var info = new ProcessStartInfo(file) { UseShellExecute = false };
    foreach (var argument in arguments)
        info.ArgumentList.Add(argument);
    using var process = Process.Start(info) ?? throw new InvalidOperationException($"{file} did not start");
    process.WaitForExit();
    if (process.ExitCode != 0)
        Fail($"{Path.GetFileName(file)} exited with {process.ExitCode}");
}

static string Fail(string message)
{
    Console.Error.WriteLine(message);
    Environment.Exit(1);
    return message;
}

static class Source
{
    /// <summary>This file, as the compiler saw it: the repo root is two folders up.</summary>
    public static string Location([CallerFilePath] string path = "") => path;
}
