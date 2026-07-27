// Docker orchestration helpers for build.cake.
// RunShellScript is loaded from ./shell.cake by build.cake.

void ComposeUp(string composeFile)
{
    var args = new ProcessArgumentBuilder();
    args.Append("compose");
    args.Append("-f");
    args.AppendQuoted(composeFile);
    args.Append("up");
    args.Append("-d");
    args.Append("--wait");
    using (var p = StartAndReturnProcess("docker", new ProcessSettings { Arguments = args }))
    {
        p.WaitForExit();
        if (p.GetExitCode() != 0)
        {
            throw new CakeException("docker compose up for " + composeFile + " failed");
        }
    }
}

void ComposeDown(string composeFile)
{
    var args = new ProcessArgumentBuilder();
    args.Append("compose");
    args.Append("-f");
    args.AppendQuoted(composeFile);
    args.Append("down");
    using (var p = StartAndReturnProcess("docker", new ProcessSettings { Arguments = args }))
    {
        p.WaitForExit();
        if (p.GetExitCode() != 0)
        {
            throw new CakeException("docker compose down for " + composeFile + " failed");
        }
    }
}

void ComposePs(string composeFile)
{
    var args = new ProcessArgumentBuilder();
    args.Append("compose");
    args.Append("-f");
    args.AppendQuoted(composeFile);
    args.Append("ps");
    using (var p = StartAndReturnProcess("docker", new ProcessSettings { Arguments = args }))
    {
        p.WaitForExit();
    }
}

void ComposeLogs(string composeFile)
{
    var args = new ProcessArgumentBuilder();
    args.Append("compose");
    args.Append("-f");
    args.AppendQuoted(composeFile);
    args.Append("logs");
    args.Append("-f");
    using (var p = StartAndReturnProcess("docker", new ProcessSettings { Arguments = args }))
    {
        p.WaitForExit();
    }
}

void EnsureDockerNetwork(string networkName)
{
    var inspectArgs = new ProcessArgumentBuilder();
    inspectArgs.Append("network");
    inspectArgs.Append("inspect");
    inspectArgs.Append(networkName);
    using (var inspect = StartAndReturnProcess("docker", new ProcessSettings { Arguments = inspectArgs }))
    {
        inspect.WaitForExit();
        if (inspect.GetExitCode() == 0)
        {
            return;
        }
    }
    Information("Creating docker network '" + networkName + "'...");
    var createArgs = new ProcessArgumentBuilder();
    createArgs.Append("network");
    createArgs.Append("create");
    createArgs.Append(networkName);
    using (var creator = StartAndReturnProcess("docker", new ProcessSettings { Arguments = createArgs }))
    {
        creator.WaitForExit();
        if (creator.GetExitCode() != 0)
        {
            throw new CakeException("docker network create " + networkName + " failed");
        }
    }
}

void WaitForPostgresSonarDatabase(int timeoutSeconds)
{
    Information("Waiting for Postgres 'sonar' database to exist...");
    var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
    while (DateTime.UtcNow < deadline)
    {
        var args = new ProcessArgumentBuilder();
        args.Append("exec");
        args.Append("postgresql");
        args.Append("psql");
        args.Append("-U");
        args.Append("postgres");
        args.Append("-d");
        args.Append("postgres");
        args.Append("-tAc");
        args.AppendQuoted("SELECT 1 FROM pg_database WHERE datname='sonar'");
        using (var process = StartAndReturnProcess("docker", new ProcessSettings { Arguments = args }))
        {
            process.WaitForExit();
            if (process.GetExitCode() == 0)
            {
                var outputs = process.GetStandardOutput();
                var first = outputs != null && outputs.Any() ? outputs.FirstOrDefault() : string.Empty;
                if ((first ?? string.Empty).Replace("\u0000", string.Empty).Trim() == "1")
                {
                    Information("Postgres: 'sonar' database ready");
                    return;
                }
            }
        }
        System.Threading.Thread.Sleep(2000);
    }
    throw new CakeException("Postgres 'sonar' database did not become ready within " + timeoutSeconds + "s");
}

void WaitForHttpOk(string url, int timeoutSeconds, int pollSeconds)
{
    Information("Waiting for " + url + " ...");
    var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
    while (DateTime.UtcNow < deadline)
    {
        var code = HttpGetStatus(url);
        if (code == 200)
        {
            Information(url + " ready (HTTP 200)");
            return;
        }
        System.Threading.Thread.Sleep(pollSeconds * 1000);
    }
    throw new CakeException(url + " did not return HTTP 200 within " + timeoutSeconds + "s");
}

int HttpGetStatus(string url)
{
    var args = new ProcessArgumentBuilder();
    args.Append("-s");
    args.Append("-o");
    args.Append("/dev/null");
    args.Append("-w");
    args.AppendQuoted("%{http_code}");
    args.Append("--max-time");
    args.Append("2");
    args.Append(url);
    using (var probe = StartAndReturnProcess("curl", new ProcessSettings
    {
        Arguments = args,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    }))
    {
        probe.WaitForExit();
        if (probe.GetExitCode() != 0)
        {
            return 0;
        }
        var lines = probe.GetStandardOutput();
        var raw = lines != null && lines.Any()
            ? (lines.FirstOrDefault() ?? string.Empty)
            : string.Empty;
        int code;
        return int.TryParse(raw.Trim(), out code) ? code : 0;
    }
}

void UpDevEnvironment(string stackCompose, string sonarCompose)
{
    EnsureDockerNetwork("cynara-net");

    var stackArgs = new ProcessArgumentBuilder();
    stackArgs.Append("compose");
    stackArgs.Append("-f");
    stackArgs.AppendQuoted(stackCompose);
    stackArgs.Append("up");
    stackArgs.Append("-d");
    stackArgs.Append("--wait");
    using (var stack = StartAndReturnProcess("docker", new ProcessSettings { Arguments = stackArgs }))
    {
        stack.WaitForExit();
        if (stack.GetExitCode() != 0)
        {
            throw new CakeException("docker compose up for " + stackCompose + " failed");
        }
    }

    WaitForPostgresSonarDatabase(120);

    var sonarArgs = new ProcessArgumentBuilder();
    sonarArgs.Append("compose");
    sonarArgs.Append("-f");
    sonarArgs.AppendQuoted(sonarCompose);
    sonarArgs.Append("up");
    sonarArgs.Append("-d");
    sonarArgs.Append("--wait");
    using (var sonar = StartAndReturnProcess("docker", new ProcessSettings { Arguments = sonarArgs }))
    {
        sonar.WaitForExit();
        if (sonar.GetExitCode() != 0)
        {
            throw new CakeException("docker compose up for " + sonarCompose + " failed");
        }
    }

    WaitForHttpOk("http://localhost:9000/api/system/status", 180, 3);
}

void BuildDockerImage(string dockerfile, string tag)
{
    var args = new ProcessArgumentBuilder();
    args.Append("build");
    args.Append("-f");
    args.AppendQuoted(dockerfile);
    args.Append("-t");
    args.Append(tag);
    args.Append(".");
    using (var p = StartAndReturnProcess("docker", new ProcessSettings { Arguments = args }))
    {
        p.WaitForExit();
        if (p.GetExitCode() != 0)
        {
            throw new CakeException("docker build failed");
        }
    }
}
