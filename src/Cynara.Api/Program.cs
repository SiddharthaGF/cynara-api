var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => Results.Ok(new
{
    service = "cynara-api",
    status = "ok",
    contract = "https://github.com/ailuracode/cynara",
}));

app.Run();

public partial class Program;
