using System.Text;

namespace AiLocal.Node.Hosting;

/// <summary>
/// Scaffolds a runnable application project (not a game) from a short prompt,
/// so the agent starts from real, buildable code instead of an empty folder.
/// The agent then fills in the actual logic via the file API.
///
/// Tech is picked from the prompt: "python" -> a Python app (main.py +
/// requirements.txt + README); "c#"/"dotnet"/".net" -> a C# console app
/// (.csproj + Program.cs). When the prompt is ambiguous the tool defaults to
/// Python (the most universally runnable choice). Like GameScaffoldService,
/// the agent is free to override by passing an explicit tech.
/// </summary>
public sealed class AppScaffoldService
{
    public record ScaffoldResult(bool Success, string Path, string Tech, string[] Files, string Output);

    public ScaffoldResult Scaffold(string tech, string prompt, string root)
    {
        tech = (tech ?? "").Trim().ToLowerInvariant();
        if (tech is "" or "auto")
            tech = PickTech(prompt);
        if (tech != "python" && tech != "csharp" && tech != "c#")
            return new(false, "", "", [], "tech maste vara 'python', 'csharp'/'c#' eller 'auto' (tomt = automatiskt val).");
        if (string.IsNullOrWhiteSpace(root))
            return new(false, "", tech, [], "root (mapp att skapa projektet i) kravs.");
        // Non-empty root -> scaffold into a fresh subfolder derived from the
        // prompt instead of refusing (same rule as GameScaffoldService).
        root = ScaffoldPaths.ForProject(root, prompt, "app");

        Directory.CreateDirectory(root);
        var files = tech == "python"
            ? ScaffoldPython(root, prompt)
            : ScaffoldCSharp(root, prompt);
        return new(true, root, tech, files, $"{tech} app skapad i {root} ({files.Length} filer).");
    }

    /// <summary>Pick the best tech for an app prompt. Python is the safe
    /// default (runs almost everywhere); C# when the prompt asks for it or
    /// implies a .NET/Windows tool.</summary>
    static string PickTech(string prompt)
    {
        var p = (prompt ?? "").ToLowerInvariant();
        if (p.Contains("c#") || p.Contains("csharp") || p.Contains("dotnet") || p.Contains(".net"))
            return "csharp";
        if (p.Contains("python") || p.Contains("py"))
            return "python";
        return "python";
    }

    static string[] ScaffoldPython(string root, string prompt)
    {
        var name = new DirectoryInfo(root).Name;
        var files = new List<string>();
        Write(root, "main.py", PythonMain(prompt));
        files.Add("main.py");
        Write(root, "requirements.txt", "# Add your dependencies here, one per line, e.g.\n# requests\n# rich\n");
        files.Add("requirements.txt");
        Write(root, "README.md",
            $"# {name}\n\nKor: `python main.py`\n\nInstallera beroenden: `pip install -r requirements.txt`\n");
        files.Add("README.md");
        return files.ToArray();
    }

    static string PythonMain(string prompt)
    {
        // A small, genuinely-runnable CLI app stub the agent extends. Keeps it
        // minimal but real: parses args, has a main(), prints something.
        var desc = (prompt ?? "").Trim();
        if (desc.Length == 0) desc = "A new Python application.";
        return
"#!/usr/bin/env python3\n" +
"# " + desc + "\n" +
"# Generated scaffold - extend main() with your actual logic.\n" +
"import argparse\n" +
"import sys\n" +
"\n" +
"\n" +
"def main() -> int:\n" +
"    parser = argparse.ArgumentParser(description=\"" + desc.Replace("\"", "'") + "\")\n" +
"    parser.add_argument(\"input\", nargs=\"?\", help=\"optional input\")\n" +
"    args = parser.parse_args()\n" +
"\n" +
"    print(\"Hello from the scaffolded app.\")\n" +
"    if args.input:\n" +
"        print(f\"Got input: {args.input}\")\n" +
"    # TODO: implement the real behaviour for: " + desc.Replace("\"", "'") + "\n" +
"    return 0\n" +
"\n" +
"\n" +
"if __name__ == \"__main__\":\n" +
"    sys.exit(main())\n";
    }

    /// <summary>Turn a folder name into a valid C# identifier for
    /// namespace/RootNamespace (e.g. "out-cs" -> "out_cs", "2play" -> "_2play").</summary>
    static string SanitizeIdentifier(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        var s = sb.ToString();
        if (s.Length == 0 || char.IsDigit(s[0])) s = "_" + s;
        return s;
    }

    static string[] ScaffoldCSharp(string root, string prompt)
    {
        var name = SanitizeIdentifier(new DirectoryInfo(root).Name);
        var files = new List<string>();
        Write(root, $"{name}.csproj", CsharpCsproj(name));
        files.Add($"{name}.csproj");
        Write(root, "Program.cs", CsharpProgram(prompt, name));
        files.Add("Program.cs");
        Write(root, "README.md",
            $"# {name}\n\nBygg och kor: `dotnet run`\n");
        files.Add("README.md");
        return files.ToArray();
    }

    static string CsharpCsproj(string name) =>
        @$"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>{name}</RootNamespace>
  </PropertyGroup>
</Project>
";

    static string CsharpProgram(string prompt, string name)
    {
        var desc = (prompt ?? "").Trim();
        if (desc.Length == 0) desc = "A new C# application.";
        var sb = new StringBuilder();
        sb.AppendLine("// " + desc);
        sb.AppendLine("// Generated scaffold - extend Main with your actual logic.");
        sb.AppendLine("using System;");
        sb.AppendLine();
        sb.AppendLine("namespace " + name + ";");
        sb.AppendLine();
        sb.AppendLine("internal static class Program");
        sb.AppendLine("{");
        sb.AppendLine("    static int Main(string[] args)");
        sb.AppendLine("    {");
        sb.AppendLine("        Console.WriteLine(\"Hello from the scaffolded app.\");");
        sb.AppendLine("        if (args.Length > 0)");
        sb.AppendLine("            Console.WriteLine($\"Got input: {string.Join(' ', args)}\");");
        sb.AppendLine("        // TODO: implement the real behaviour for: " + desc);
        sb.AppendLine("        return 0;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    static void Write(string root, string rel, string content)
    {
        var path = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
