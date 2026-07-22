using Cynara.Application.Modules.FormAi;

using Microsoft.Extensions.Logging;

namespace Cynara.Infrastructure.Modules.FormAi;

/// <summary>
/// Loads and caches the <c>form-schema-authoring</c> skill
/// (<c>.cursor/skills/form-schema-authoring/</c>) from disk.
///
/// The skill directory is searched up from <see cref="AppContext.BaseDirectory"/>
/// so it works regardless of the current working directory used at launch
/// (<c>dotnet run</c> from repo root, <c>pnpm dev</c>, or tests under
/// <c>tests/Cynara.Api.Tests</c>).
/// </summary>
public sealed class FileFormAiSkillLoader : IFormAiSkillLoader
{
    private const string SkillRootRelativePath = ".cursor/skills/form-schema-authoring";

    private static readonly string[] ReferenceFileOrder =
    [
        "engine-features.md",
        "unsupported-features.md",
        "validation-checklist.md",
        "docs.md",
    ];

    private static readonly string[] AssetFileOrder =
    [
        "output-template.json",
        "widget-map.json",
        "rules-examples.json",
    ];

    private readonly Lazy<string> _cachedBody;
    private readonly ILogger<FileFormAiSkillLoader> _logger;

    public FileFormAiSkillLoader(ILogger<FileFormAiSkillLoader> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _cachedBody = new Lazy<string>(LoadAndConcatenate, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string GetSkillBody()
    {
        return _cachedBody.Value;
    }

    private string LoadAndConcatenate()
    {
        string? skillRoot = LocateSkillDirectory();
        if (skillRoot is null)
        {
            _logger.LogWarning(
                "form-schema-authoring skill not found under any parent of {BaseDir} or cwd {Cwd}; chat will run with a degraded prompt.",
                AppContext.BaseDirectory, Directory.GetCurrentDirectory());
            return string.Empty;
        }

        var sections = new List<string>();
        string skillPath = Path.Combine(skillRoot, "SKILL.md");
        if (File.Exists(skillPath))
        {
            sections.Add($"## SKILL.md ({Path.GetRelativePath(AppContext.BaseDirectory, skillPath)})");
            sections.Add(ReadAll(skillPath));
        }

        string referencesDir = Path.Combine(skillRoot, "references");
        if (Directory.Exists(referencesDir))
        {
            foreach (string name in ReferenceFileOrder)
            {
                string path = Path.Combine(referencesDir, name);
                if (File.Exists(path))
                {
                    sections.Add($"## references/{name}");
                    sections.Add(ReadAll(path));
                }
            }
        }

        string assetsDir = Path.Combine(skillRoot, "assets");
        if (Directory.Exists(assetsDir))
        {
            foreach (string name in AssetFileOrder)
            {
                string path = Path.Combine(assetsDir, name);
                if (File.Exists(path))
                {
                    string content = ReadAll(path);
                    string relative = Path.GetRelativePath(AppContext.BaseDirectory, path);
                    sections.Add($"## assets/{name} (file: {relative})");
                    sections.Add($"```json\n{content}\n```");
                }
            }
        }

        return string.Join("\n\n", sections);
    }

    private static string ReadAll(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static string? LocateSkillDirectory()
    {
        foreach (DirectoryInfo start in EnumerateCandidateRoots())
        {
            for (DirectoryInfo? dir = start; dir is not null; dir = dir.Parent)
            {
                string skillDir = Path.Combine(dir.FullName, SkillRootRelativePath);
                if (Directory.Exists(skillDir)
                    && File.Exists(Path.Combine(skillDir, "SKILL.md")))
                {
                    return skillDir;
                }
            }
        }

        return null;
    }

    private static IEnumerable<DirectoryInfo> EnumerateCandidateRoots()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        DirectoryInfo? baseDir = SafeDir(AppContext.BaseDirectory);
        if (baseDir is not null && seen.Add(baseDir.FullName))
        {
            yield return baseDir;
        }

        DirectoryInfo? cwd = SafeDir(Directory.GetCurrentDirectory());
        if (cwd is not null && seen.Add(cwd.FullName))
        {
            yield return cwd;
        }
    }

    private static DirectoryInfo? SafeDir(string path)
    {
        try
        {
            return new DirectoryInfo(path);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (PathTooLongException)
        {
            return null;
        }
    }
}
