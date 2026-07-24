using System.Text.Json.Nodes;

using Cynara.Application.Modules.FormAi;

namespace Cynara.Api.Tests;

public sealed class FormAiSanitizerTests
{
    [Fact]
    public void Sanitize_PreservesAllowedSectionAndRepeaterMetadataWithinLimits()
    {
        string description = new('d', 1001);
        string addButtonLabel = new('a', 129);
        string removeButtonLabel = new('r', 129);
        JsonObject clinical = new()
        {
            ["schemaVersion"] = "1.0.0",
            ["fields"] = new JsonArray
            {
                new JsonObject
                {
                    ["id"] = "medications",
                    ["code"] = "medications",
                    ["type"] = "repeater",
                    ["items"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["id"] = "medication-name",
                            ["code"] = "medication.name",
                            ["type"] = "text",
                        },
                    },
                },
            },
        };
        JsonObject ui = new()
        {
            ["schemaVersion"] = "1.0.0",
            ["clinicalSchemaVersion"] = "1.0.0",
            ["fields"] = new JsonObject
            {
                ["medications"] = new JsonObject
                {
                    ["label"] = "Medications",
                    ["widget"] = "repeater",
                },
                ["medication-name"] = new JsonObject
                {
                    ["label"] = "Name",
                    ["widget"] = "text-input",
                },
            },
            ["layout"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "section",
                    ["title"] = "Medication list",
                    ["description"] = description,
                    ["unknown"] = "drop this",
                    ["children"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["type"] = "repeater",
                            ["fieldId"] = "medications",
                            ["addButtonLabel"] = addButtonLabel,
                            ["removeButtonLabel"] = removeButtonLabel,
                            ["unknown"] = "drop this too",
                            ["itemTemplate"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["type"] = "field",
                                    ["fieldId"] = "medication-name",
                                },
                            },
                        },
                    },
                },
            },
        };

        SanitizedAiTriple sanitized = FormAiSanitizer.Sanitize(
            clinical,
            ui,
            new JsonObject());

        JsonObject section = (sanitized.Ui["layout"] as JsonArray)![0]!.AsObject();
        JsonObject repeater = (section["children"] as JsonArray)![0]!.AsObject();
        Assert.Equal(new string('d', 1000), section["description"]!.GetValue<string>());
        Assert.Equal(new string('a', 128), repeater["addButtonLabel"]!.GetValue<string>());
        Assert.Equal(new string('r', 128), repeater["removeButtonLabel"]!.GetValue<string>());
        Assert.Null(section["unknown"]);
        Assert.Null(repeater["unknown"]);
    }
}
