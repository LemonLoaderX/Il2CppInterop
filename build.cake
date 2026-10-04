#addin nuget:?package=Cake.FileHelpers&version=4.0.1
#addin nuget:?package=SharpZipLib&version=1.3.1
#addin nuget:?package=Cake.Compression&version=0.2.6
#addin nuget:?package=Cake.Json&version=6.0.1
#addin nuget:?package=Newtonsoft.Json&version=13.0.1

var target = Argument("target", "Build");
var buildVersion = Argument("build_version", "");
var buildTag = Argument("build_tag", "");
var projects = new[] { "Il2CppInterop.CLI", "Il2CppInterop.Common", "Il2CppInterop.Generator",
    "Il2CppInterop.Runtime", "Il2CppInterop.HarmonySupport", "Il2CppInterop.StructGenerator" };

string RunGit(string command, string separator = "")
{
    using(var process = StartAndReturnProcess("git", new ProcessSettings { Arguments = command, RedirectStandardOutput = true }))
    {
        process.WaitForExit();
        return string.Join(separator, process.GetStandardOutput());
    }
}

Task("Build")
    .Does(() =>
{
    var msBuildSettings = new DotNetCoreMSBuildSettings();
    if (!string.IsNullOrEmpty(buildVersion))
        msBuildSettings.Properties["VersionPrefix"] = new string[]{buildVersion};
    if (!string.IsNullOrEmpty(buildTag))
        msBuildSettings.Properties["VersionSuffix"] = new string[]{buildTag};
    var buildSettings = new DotNetCoreBuildSettings {
        Configuration = "Release",
		MSBuildSettings = msBuildSettings
    };

    DotNetCoreBuild(".", buildSettings);
});

Task("Test")
    .IsDependentOn("Build")
    .Does(() =>
{
    foreach (var project in new[] { "Il2CppInterop.Generator.Tests", "Il2CppInterop.Runtime.Tests" }) {
        var result = StartProcess("dotnet", new ProcessSettings {
            Arguments = $"run --project {project}/{project}.csproj --configuration Release -p:GeneratePackageOnBuild=false" +
                (string.IsNullOrEmpty(buildVersion) ? "" : $" -p:VersionPrefix={buildVersion}") +
                (string.IsNullOrEmpty(buildTag) ? "" : $" -p:VersionSuffix={buildTag}")
        });
        if (result != 0)
            throw new Exception($"{project} failed with exit code {result}.");
    }
});

Task("Pack")
    .IsDependentOn("Test")
    .Does(() =>
{
    var distDir = Directory("./bin/zip");
    CreateDirectory(distDir);

    var versionString = string.IsNullOrEmpty(buildVersion) ? "" : $".{buildVersion}";
    if (!string.IsNullOrEmpty(buildVersion) && !string.IsNullOrEmpty(buildTag))
        versionString += $"-{buildTag}";
    foreach (var project in projects) {
        var dir = Directory($"./bin/{project}");
        CopyFileToDirectory("LICENSE", dir);
        CopyFileToDirectory("PATCHES.md", dir);
        ZipCompress(dir, distDir + File($"{project}{versionString}.zip"));
    }
    var packages = Directory("./bin/release-nuget");
    EnsureDirectoryExists(packages);
    CleanDirectory(packages);
    foreach (var project in projects) {
        if (project == "Il2CppInterop.StructGenerator") continue; // Bundled in the CLI tool package.
        var matches = GetFiles($"./bin/NuGet/{project}.*.nupkg");
        var copied = 0;
        foreach (var package in matches) {
            if (!string.IsNullOrEmpty(buildVersion) && package.GetFilename().FullPath !=
                $"{project}.{buildVersion}{(string.IsNullOrEmpty(buildTag) ? "" : "-" + buildTag)}.nupkg") continue;
            CopyFileToDirectory(package, packages);
            copied++;
        }
        if (copied == 0 || (!string.IsNullOrEmpty(buildVersion) && copied != 1))
            throw new Exception($"Expected a package for {project} at the selected release version.");
    }
    CopyFileToDirectory("LICENSE", packages);
    ZipCompress(packages, distDir + File($"Il2CppInterop.NuGet{versionString}.zip"));
});

RunTarget(target);
