using System.Text.Json;

using Cynara.Domain.Capabilities;

namespace Cynara.Application.Modules.Invitations;

/// <summary>
/// Defensive parser for stored <c>ProfileSnapshot</c> payloads. Extracts
/// the actor id (1..128 characters) and the capability codes that are
/// granted at acceptance; every malformed or unsupported shape collapses
/// to <see langword="null"/> so the workflow fails closed. The capability
/// catalog gate uses <see cref="CapabilityCodes.All"/> as the single
/// source of truth — no schema drift is possible.
/// </summary>
public static class InvitationProfileSnapshotParser
{
    private const int MaxActorIdLength = 128;

    private const int MaxCapabilityLength = 64;

    public static ParsedProfileSnapshot? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!root.TryGetProperty("actorId", out JsonElement actorIdElement)
                || actorIdElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string actorId = actorIdElement.GetString() ?? string.Empty;
            if (actorId.Length is 0 or > MaxActorIdLength)
            {
                return null;
            }

            if (!root.TryGetProperty(
                    "capabilities",
                    out JsonElement capabilitiesElement)
                || capabilitiesElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            List<string> capabilities = [];
            foreach (JsonElement item in capabilitiesElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    return null;
                }

                string code = item.GetString() ?? string.Empty;
                if (code.Length is 0 or > MaxCapabilityLength
                    || !CapabilityCodes.All.Contains(code))
                {
                    return null;
                }

                capabilities.Add(code);
            }

            return new ParsedProfileSnapshot(actorId, capabilities);
        }
    }
}