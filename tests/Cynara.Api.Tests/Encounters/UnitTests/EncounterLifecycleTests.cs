using Cynara.Application;
using Cynara.Application.Common;
using Cynara.Application.Modules.Encounters;
using Cynara.Domain.Encounters;

namespace Cynara.Api.Tests.Encounters.UnitTests;

/// <summary>
/// Unit coverage for the encounter lifecycle state machine.
/// </summary>
public sealed class EncounterLifecycleUnitTests
{
    [Fact]
    public void Fire_Complete_FromOpen_TransitionsToCompleted()
    {
        Encounter encounter = OpenEncounter();
        EncounterLifecycle.Fire(encounter, TerminalLifecycle.Trigger.Complete);
        Assert.Equal(EncounterStatus.Completed, encounter.Status);
    }

    [Fact]
    public void Fire_Cancel_FromOpen_TransitionsToCanceled()
    {
        Encounter encounter = OpenEncounter();
        EncounterLifecycle.Fire(encounter, TerminalLifecycle.Trigger.Cancel);
        Assert.Equal(EncounterStatus.Canceled, encounter.Status);
    }

    [Fact]
    public void Fire_EnterInError_FromOpen_TransitionsToEnteredInError()
    {
        Encounter encounter = OpenEncounter();
        EncounterLifecycle.Fire(
            encounter, TerminalLifecycle.Trigger.EnterInError);
        Assert.Equal(EncounterStatus.EnteredInError, encounter.Status);
    }

    [Fact]
    public void Fire_FromCompleted_ThrowsInvalidState()
    {
        Encounter encounter = new()
        {
            ResponsibleProfessionalId = "dr-who",
            Status = EncounterStatus.Completed,
        };

        InvalidStateException ex = Assert.Throws<InvalidStateException>(
            () => EncounterLifecycle.Fire(
                encounter, TerminalLifecycle.Trigger.Cancel));

        Assert.Contains("completed", ex.Message, StringComparison.Ordinal);
        Assert.Equal(EncounterStatus.Completed, encounter.Status);
    }

    private static Encounter OpenEncounter()
    {
        return new Encounter
        {
            ResponsibleProfessionalId = "dr-who",
            Status = EncounterStatus.Open,
        };
    }
}
