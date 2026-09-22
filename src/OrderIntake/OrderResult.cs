namespace OrderIntake;

/// <summary>
/// The outcome of processing one order: either an Accepted order with no
/// errors, or a Rejected result carrying one or more errors (and no order).
/// </summary>
public sealed class OrderResult
{
    public OrderStatus Status { get; }
    public LabOrder? Order { get; }
    public IReadOnlyList<ValidationError> Errors { get; }

    private OrderResult(OrderStatus status, LabOrder? order, IReadOnlyList<ValidationError> errors)
    {
        Status = status;
        Order = order;
        Errors = errors;
    }

    public static OrderResult Accepted(LabOrder order) =>
        new(OrderStatus.Accepted, order, Array.Empty<ValidationError>());

    public static OrderResult Rejected(IReadOnlyList<ValidationError> errors) =>
        new(OrderStatus.Rejected, null, errors);

    /// <summary>
    /// Rejected (malformed input): JSON that could not be read into an order
    /// at all. Always carries exactly one MALFORMED_INPUT error on field "$".
    /// </summary>
    public static OrderResult Malformed(string message) =>
        new(OrderStatus.Rejected, null,
            new[] { new ValidationError("$", "MALFORMED_INPUT", message) });
}
