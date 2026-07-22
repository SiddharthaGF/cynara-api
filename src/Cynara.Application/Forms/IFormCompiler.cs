namespace Cynara.Application.Forms;

public interface IFormCompiler
{
    public Task<FormCompilationResult> CompileAsync(
        string clinicalSchemaJson,
        string? uiSchemaJson,
        string? rulesSchemaJson,
        CancellationToken cancellationToken);
}
