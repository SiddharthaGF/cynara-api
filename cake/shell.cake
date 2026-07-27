// Common shell helpers shared across build.cake partials.

void RunShellScript(string scriptPath)
{
    var full = System.IO.Path.GetFullPath(scriptPath);

    var chmod = StartAndReturnProcess("chmod", new ProcessSettings { Arguments = "+x " + full });
    chmod.WaitForExit();

    using (var p = StartAndReturnProcess("bash", new ProcessSettings
    {
        Arguments = new ProcessArgumentBuilder()
            .Append("-lc")
            .AppendQuoted("\"" + full + "\"")
    }))
    {
        p.WaitForExit();
        if (p.GetExitCode() != 0)
        {
            throw new CakeException("Script " + full + " failed with exit code " + p.GetExitCode().ToString());
        }
    }
}
