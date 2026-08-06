#load "./cake/shell.cake"
#load "./cake/docker.cake"

var solution = Argument<string>("solution", EnvironmentVariable("SOLUTION") ?? "Cynara.Api.sln");
var configuration = Argument<string>("configuration", EnvironmentVariable("CONFIGURATION") ?? "Debug");
var seedArgs = Argument<string>("seed-args", EnvironmentVariable("SEED_ARGS") ?? string.Empty);

var includeRaw = Argument<string>("include", EnvironmentVariable("FORMAT_INCLUDE") ?? string.Empty) ?? string.Empty;
var formatInclude = includeRaw
    .Split(new[] { ',', ';', ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
    .ToList();

var sonarCompose = "docker/sonarqube/docker-compose.yml";
var stackCompose = "docker/stack.yml";
var dockerfile = "docker/Dockerfile";
var imageName = "cynara-api";
var sonarScriptsDir = "scripts";

Task("Restore")
    .Description("Restore the .NET solution")
    .Does(() =>
    {
        DotNetRestore(solution);
    });

Task("Format")
    .Description("Write formatting changes (dotnet format)")
    .Does(() =>
    {
        var settings = new DotNetFormatSettings();
        if (formatInclude.Count > 0)
        {
            settings.Include = formatInclude;
        }
        DotNetFormat(solution, settings);
    });

Task("FormatCheck")
    .Description("Verify formatting only (dotnet format --verify-no-changes)")
    .Does(() =>
    {
        DotNetFormat(solution, new DotNetFormatSettings
        {
            VerifyNoChanges = true
        });
    });

Task("Lint")
    .Description("Build with warnings as errors")
    .IsDependentOn("Restore")
    .Does(() =>
    {
        DotNetBuild(solution, new DotNetBuildSettings
        {
            NoRestore = true,
            Configuration = configuration,
            Verbosity = DotNetVerbosity.Normal,
            MSBuildSettings = new DotNetMSBuildSettings
            {
                TreatAllWarningsAs = MSBuildTreatAllWarningsAs.Error
            }
        });
    });

Task("LintFix")
    .Description("Apply safe analyzer fixes then lint")
    .IsDependentOn("Restore")
    .Does(() =>
    {
        DotNetFormatAnalyzers(solution);
        RunTarget("Lint");
    });

Task("Test")
    .Description("Run the test suite (excludes Category=E2E)")
    .IsDependentOn("Restore")
    .Does(() =>
    {
        DotNetTest(solution, new DotNetTestSettings
        {
            NoRestore = true,
            Configuration = configuration,
            Verbosity = DotNetVerbosity.Normal,
            Filter = "Category!=E2E",
            Loggers = new[] { "console;verbosity=normal" }
        });
    });

Task("OpenApiExport")
    .Description("Regenerate contracts/openapi.json via tools/Cynara.OpenApiExport")
    .IsDependentOn("Restore")
    .Does(() =>
    {
        var args = new ProcessArgumentBuilder();
        args.Append("--project");
        args.AppendQuoted("tools/Cynara.OpenApiExport");
        args.Append("-c");
        args.Append(configuration);
        args.Append("--");
        args.Append("--output");
        args.Append("contracts/openapi.json");
        DotNetRun("tools/Cynara.OpenApiExport/Cynara.OpenApiExport.csproj", args, new DotNetRunSettings
        {
            NoRestore = true,
            Configuration = configuration
        });
    });

Task("OpenApiCheck")
    .Description("Fail if a fresh export differs from the committed contract")
    .IsDependentOn("Restore")
    .Does(() =>
    {
        var temp = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "cynara-openapi-" + Guid.NewGuid().ToString("N") + ".json");
        var args = new ProcessArgumentBuilder();
        args.Append("--project");
        args.AppendQuoted("tools/Cynara.OpenApiExport");
        args.Append("-c");
        args.Append(configuration);
        args.Append("--");
        args.Append("--output");
        args.AppendQuoted(temp);
        DotNetRun("tools/Cynara.OpenApiExport/Cynara.OpenApiExport.csproj", args, new DotNetRunSettings
        {
            NoRestore = true,
            Configuration = configuration
        });
        var committed = System.IO.File.ReadAllText("contracts/openapi.json");
        var exported = System.IO.File.ReadAllText(temp);
        System.IO.File.Delete(temp);
        if (!string.Equals(committed, exported, System.StringComparison.Ordinal))
        {
            throw new CakeException(
                "OpenAPI contract drift detected. Run `dotnet cake --target=OpenApiExport` "
                + "and commit the regenerated contracts/openapi.json together with the "
                + "endpoint or schema change.");
        }
    });

Task("Check")
    .Description("Restore + format-check + lint + openapi-check + test")
    .IsDependentOn("Restore")
    .IsDependentOn("FormatCheck")
    .IsDependentOn("Lint")
    .IsDependentOn("OpenApiCheck")
    .IsDependentOn("Test");

Task("Fix")
    .Description("Format + apply safe analyzer + style fixes")
    .IsDependentOn("Restore")
    .Does(() =>
    {
        DotNetFormat(solution);
        DotNetFormatStyle(solution);
        DotNetFormatAnalyzers(solution);
    });

Task("Seed")
    .Description("Seed the demo showcase via Application services")
    .IsDependentOn("Restore")
    .Does(() =>
    {
        var args = new ProcessArgumentBuilder();
        args.Append("--project");
        args.AppendQuoted("tools/Cynara.Seed");
        args.Append("-c");
        args.Append(configuration);
        if (!string.IsNullOrWhiteSpace(seedArgs))
        {
            args.Append("--");
            foreach (var piece in seedArgs.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                args.Append(piece);
            }
        }
        DotNetRun("tools/Cynara.Seed/Cynara.Seed.csproj", args, new DotNetRunSettings
        {
            NoRestore = true,
            Configuration = configuration
        });
    });

Task("SonarUp")
    .Description("Bring up local SonarQube + Postgres")
    .Does(() => ComposeUp(sonarCompose));

Task("SonarDown")
    .Description("Stop local SonarQube + Postgres (volumes preserved)")
    .Does(() => ComposeDown(sonarCompose));

Task("SonarBootstrap")
    .Description("Bootstrap SonarQube: change admin password and write .sonar/token")
    .IsDependentOn("SonarUp")
    .Does(() =>
    {
        RunShellScript(System.IO.Path.Combine(sonarScriptsDir, "sonar-bootstrap.sh"));
    });

Task("SonarScan")
    .Description("Run SonarScanner for .NET against the local server")
    .Does(() =>
    {
        RunShellScript(System.IO.Path.Combine(sonarScriptsDir, "sonar-scan.sh"));
    });

Task("Sonar")
    .Description("Up + bootstrap + scan the local SonarQube Community Build")
    .IsDependentOn("SonarBootstrap")
    .IsDependentOn("SonarScan");

Task("Up")
    .Description("Bring up Postgres + pgAdmin + SonarQube stacks with shared network")
    .Does(() => UpDevEnvironment(stackCompose, sonarCompose));

Task("Down")
    .Description("Stop Postgres + pgAdmin + SonarQube stacks (volumes preserved)")
    .Does(() =>
    {
        ComposeDown(stackCompose);
        ComposeDown(sonarCompose);
    });

Task("Status")
    .Description("Print container status for both stacks")
    .Does(() =>
    {
        ComposePs(stackCompose);
        ComposePs(sonarCompose);
    });

Task("StackLogs")
    .Description("Tail logs from both stacks")
    .Does(() =>
    {
        ComposeLogs(stackCompose);
        ComposeLogs(sonarCompose);
    });

Task("ImageBuild")
    .Description("Build the API Docker image")
    .Does(() => BuildDockerImage(dockerfile, imageName));

Task("Default")
    .Description("Default task alias for Check")
    .IsDependentOn("Check");

RunTarget(Argument<string>("target", "Default"));
