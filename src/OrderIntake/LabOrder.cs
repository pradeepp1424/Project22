namespace OrderIntake;

/// <summary>
/// A fully validated laboratory order. Only ever constructed when every
/// validation rule in the spec has passed.
/// </summary>
public sealed class LabOrder
{
    public required string OrderId { get; init; }
    public required string PatientId { get; init; }
    public required string SpecimenId { get; init; }

    /// <summary>Normalized fixed form, e.g. "Blood".</summary>
    public required string SpecimenType { get; init; }

    /// <summary>Normalized fixed form, e.g. "Urgent".</summary>
    public required string Priority { get; init; }

    public required DateOnly CollectionDate { get; init; }

    /// <summary>Requested test names, original sender casing preserved.</summary>
    public required IReadOnlyList<string> RequestedTests { get; init; }
}
