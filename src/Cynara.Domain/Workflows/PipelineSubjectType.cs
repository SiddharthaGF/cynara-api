namespace Cynara.Domain.Workflows;

/// <summary>
/// The clinical record a pipeline drives: an encounter or a patient record.
/// </summary>
public enum PipelineSubjectType
{
    Encounter = 0,
    Patient = 1,
}
