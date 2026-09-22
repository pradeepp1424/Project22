using OrderIntake;
using Xunit;

namespace OrderIntake.Tests;

public class OrderIntakeServiceTests
{
    private readonly OrderIntakeService _service = new();

    // ---------------------------------------------------------------------
    // Group 1: Accepted order
    // ---------------------------------------------------------------------

    [Fact]
    public void Process_ValidOrderWithMixedCaseAndUnknownField_IsAcceptedAndNormalized()
    {
        const string json = """
        {
          "orderId": "ORD-1005",
          "patientId": "PAT-505",
          "specimenId": "SP-9005",
          "specimenType": "bLOOd",
          "priority": "URGENT",
          "collectionDate": "2026-09-18",
          "requestedTests": ["Glucose", "CompleteBloodCount"],
          "senderNote": "ignore me"
        }
        """;

        OrderResult result = _service.Process(json);

        Assert.Equal(OrderStatus.Accepted, result.Status);
        Assert.Empty(result.Errors);
        Assert.NotNull(result.Order);

        Assert.Equal("ORD-1005", result.Order!.OrderId);
        Assert.Equal("PAT-505", result.Order.PatientId);
        Assert.Equal("SP-9005", result.Order.SpecimenId);
        Assert.Equal("Blood", result.Order.SpecimenType);   // normalized fixed form
        Assert.Equal("Urgent", result.Order.Priority);       // normalized fixed form
        Assert.Equal(new DateOnly(2026, 9, 18), result.Order.CollectionDate);
        Assert.Equal(new[] { "Glucose", "CompleteBloodCount" }, result.Order.RequestedTests);
    }

    [Theory]
    [InlineData("blood", "Blood")]
    [InlineData("BLOOD", "Blood")]
    [InlineData("Blood", "Blood")]
    [InlineData("urine", "Urine")]
    [InlineData("TISSUE", "Tissue")]
    [InlineData("saliva", "Saliva")]
    public void Process_SpecimenTypeAnyCasing_NormalizesToFixedForm(string input, string expected)
    {
        string json = BuildValidOrderJson(specimenType: input);

        OrderResult result = _service.Process(json);

        Assert.Equal(OrderStatus.Accepted, result.Status);
        Assert.Equal(expected, result.Order!.SpecimenType);
    }

    // ---------------------------------------------------------------------
    // Group 2: All errors at once
    // ---------------------------------------------------------------------

    [Fact]
    public void Process_OrderWithMultipleFailures_ReturnsCompleteErrorSet()
    {
        const string json = """
        {
          "orderId": "   ",
          "patientId": "PAT-505",
          "specimenId": "SP-9005",
          "specimenType": "Plasma",
          "priority": "Stat",
          "collectionDate": "2026/09/18",
          "requestedTests": []
        }
        """;

        OrderResult result = _service.Process(json);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Null(result.Order);

        var actual = result.Errors.Select(e => (e.Field, e.Code)).ToHashSet();
        var expected = new HashSet<(string Field, string Code)>
        {
            ("orderId", "REQUIRED"),
            ("specimenType", "INVALID_VALUE"),
            ("priority", "INVALID_VALUE"),
            ("collectionDate", "INVALID_FORMAT"),
            ("requestedTests", "REQUIRED"),
        };

        Assert.Equal(expected, actual);
        Assert.Equal(expected.Count, result.Errors.Count);
    }

    // ---------------------------------------------------------------------
    // Group 3: ID length
    // ---------------------------------------------------------------------

    [Fact]
    public void Process_OrderIdExactlyTwentyCharacters_IsAccepted()
    {
        string id20 = new string('A', 20);
        string json = BuildValidOrderJson(orderId: id20);

        OrderResult result = _service.Process(json);

        Assert.Equal(OrderStatus.Accepted, result.Status);
        Assert.Equal(id20, result.Order!.OrderId);
    }

    [Fact]
    public void Process_OrderIdTwentyOneCharacters_ReturnsMaxLength()
    {
        string id21 = new string('A', 21);
        string json = BuildValidOrderJson(orderId: id21);

        OrderResult result = _service.Process(json);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Contains(result.Errors, e => e.Field == "orderId" && e.Code == "MAX_LENGTH");
    }

    // ---------------------------------------------------------------------
    // Group 4: Collection date
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("2026-02-30")]   // not a real calendar date
    [InlineData("2026-9-2")]     // missing leading zeros
    [InlineData("20-09-2026")]   // wrong shape
    [InlineData("2026/09/20")]   // wrong separator
    public void Process_InvalidCollectionDateFormats_ReturnsInvalidFormat(string badDate)
    {
        string json = BuildValidOrderJson(collectionDate: badDate);

        OrderResult result = _service.Process(json);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Contains(result.Errors, e => e.Field == "collectionDate" && e.Code == "INVALID_FORMAT");
    }

    [Fact]
    public void Process_CollectionDateInTheFuture_ReturnsFutureDate()
    {
        // Computed from today, never hardcoded, so the test stays valid over time.
        string tomorrow = DateOnly.FromDateTime(DateTime.Today).AddDays(1).ToString("yyyy-MM-dd");
        string json = BuildValidOrderJson(collectionDate: tomorrow);

        OrderResult result = _service.Process(json);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Contains(result.Errors, e => e.Field == "collectionDate" && e.Code == "FUTURE_DATE");
    }

    [Fact]
    public void Process_CollectionDateIsToday_IsAccepted()
    {
        string today = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        string json = BuildValidOrderJson(collectionDate: today);

        OrderResult result = _service.Process(json);

        Assert.Equal(OrderStatus.Accepted, result.Status);
    }

    // ---------------------------------------------------------------------
    // Group 5: Requested tests
    // ---------------------------------------------------------------------

    [Fact]
    public void Process_EmptyRequestedTests_ReturnsRequired()
    {
        string json = BuildValidOrderJson(requestedTests: "[]");

        OrderResult result = _service.Process(json);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Contains(result.Errors, e => e.Field == "requestedTests" && e.Code == "REQUIRED");
    }

    [Fact]
    public void Process_RequestedTestsDifferingOnlyByCase_ReturnsDuplicate()
    {
        string json = BuildValidOrderJson(requestedTests: "[\"Glucose\", \"glucose\", \"GLUCOSE\"]");

        OrderResult result = _service.Process(json);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Contains(result.Errors, e => e.Field == "requestedTests" && e.Code == "DUPLICATE");
        // One DUPLICATE error is enough, even with three colliding entries.
        Assert.Single(result.Errors, e => e.Code == "DUPLICATE");
    }

    [Fact]
    public void Process_RequestedTestsWithEmptyItem_ReturnsInvalidValue()
    {
        string json = BuildValidOrderJson(requestedTests: "[\"Glucose\", \"\"]");

        OrderResult result = _service.Process(json);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Contains(result.Errors, e => e.Field == "requestedTests" && e.Code == "INVALID_VALUE");
    }

    // ---------------------------------------------------------------------
    // Group 6: Broken / malformed JSON
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("{not valid json")]
    [InlineData("[]")]
    [InlineData("\"just a string\"")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("   ")]
    public void Process_MalformedOrNonObjectInput_ReturnsSingleMalformedError(string badInput)
    {
        OrderResult result = _service.Process(badInput);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Null(result.Order);
        Assert.Single(result.Errors);
        Assert.Equal("$", result.Errors[0].Field);
        Assert.Equal("MALFORMED_INPUT", result.Errors[0].Code);
    }

    [Fact]
    public void Process_NullStringInput_DoesNotThrow()
    {
        OrderResult result = _service.Process(null);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Equal("MALFORMED_INPUT", result.Errors[0].Code);
    }

    [Fact]
    public void Process_FieldWithIncompatibleJsonType_ReturnsSingleMalformedError()
    {
        // orderId is a number instead of a string - a shape problem, not a value problem.
        const string json = """{ "orderId": 123, "patientId": "PAT-1" }""";

        OrderResult result = _service.Process(json);

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.Single(result.Errors);
        Assert.Equal("MALFORMED_INPUT", result.Errors[0].Code);
    }

    [Fact]
    public void Process_EmptyObject_ReturnsFieldErrorsNotMalformed()
    {
        OrderResult result = _service.Process("{}");

        Assert.Equal(OrderStatus.Rejected, result.Status);
        Assert.DoesNotContain(result.Errors, e => e.Code == "MALFORMED_INPUT");
        Assert.True(result.Errors.Count >= 5); // REQUIRED for every required field
    }

    // ---------------------------------------------------------------------
    // Test helper
    // ---------------------------------------------------------------------

    private static string BuildValidOrderJson(
        string orderId = "ORD-1005",
        string specimenType = "Blood",
        string priority = "Urgent",
        string collectionDate = "2026-01-01",
        string requestedTests = "[\"Glucose\"]")
    {
        return $$"""
        {
          "orderId": "{{orderId}}",
          "patientId": "PAT-505",
          "specimenId": "SP-9005",
          "specimenType": "{{specimenType}}",
          "priority": "{{priority}}",
          "collectionDate": "{{collectionDate}}",
          "requestedTests": {{requestedTests}}
        }
        """;
    }
}
