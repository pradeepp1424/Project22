# Project22 — Laboratory Order Intake and Input Validation Service

A small C# class library that validates a single laboratory order supplied as
a JSON string and returns a typed result: Accepted (with the parsed order) or
Rejected (with the full list of validation errors). No console app, web API,
database, or UI — as specified.

## Prerequisites

- .NET 10 SDK

## Build

```bash
dotnet build
```

## Test

```bash
dotnet test
```

## Run / use it

There's no CLI or API by design. To exercise it directly, reference the
`OrderIntake` project and call the single public entry point:

```csharp
using OrderIntake;

var service = new OrderIntakeService();
OrderResult result = service.Process(jsonString);

if (result.Status == OrderStatus.Accepted)
{
    // use result.Order (typed LabOrder)
}
else
{
    // inspect result.Errors (list of ValidationError: Field, Code, Message)
}
```

The included test project (`dotnet test`) doubles as a set of runnable,
concrete usage examples for every rule in the spec.

## Project layout

```
Project22.sln
src/OrderIntake/
  OrderIntakeService.cs   # single public method: Process(string) -> OrderResult
  LabOrder.cs              # the typed, accepted order
  OrderResult.cs           # Accepted / Rejected / Malformed wrapper
  ValidationError.cs       # Field + Code + Message
  OrderStatus.cs
tests/OrderIntake.Tests/
  OrderIntakeServiceTests.cs   # six test groups per Section 5 of the spec
```

## Design summary

- `Process(string json)` is the one public method. Internally it is split
  into four steps that stay in separate, single-purpose methods: (1) parse
  the JSON and confirm it's an object with compatible field types, (2) run
  per-field validation rules and collect *every* error, (3) build the typed
  `LabOrder` only if there were zero errors, (4) wrap everything in an
  `OrderResult`.
- JSON is read with `System.Text.Json.JsonDocument` rather than deserializing
  straight into a model class. This is what lets the service tell the
  difference between "field is missing/null" (a field-level `REQUIRED`
  error) and "field has the wrong JSON type entirely" (a whole-message
  `MALFORMED_INPUT`), which a plain POCO deserializer collapses together or
  throws an exception on.
- `specimenType` and `priority` are matched against a
  case-insensitive lookup table and normalized to their fixed form (e.g.
  `Blood`, `Urgent`) before being stored on the returned `LabOrder`.
- `collectionDate` is checked against a strict `^\d{4}-\d{2}-\d{2}$` regex
  first (so `2026-9-2` is rejected even though .NET's exact-date parser can
  be lenient about missing leading zeros on some platforms), then parsed
  with `DateOnly.TryParseExact` to catch impossible calendar dates like
  `2026-02-30`. `FUTURE_DATE` is only checked once the date has parsed
  successfully, as required.
- The service never throws for bad input. JSON parse failures, a non-object
  root, and field type mismatches are all caught and converted into a single
  `MALFORMED_INPUT` error on field `"$"`.

## Assumptions and decisions (where the spec allows a sensible call)

- **No trimming.** String field values are used exactly as received (aside
  from the `IsNullOrWhiteSpace` check used to decide "empty/blank" for
  `REQUIRED`). A value like `" ORD-1"` with a leading space is treated as-is
  and counted toward the 20-character max length. The spec explicitly says
  trimming is optional, so this keeps behavior simple and predictable.
- **Type mismatches on any recognized field are treated as `MALFORMED_INPUT`
  for the whole message**, not just for `orderId` (the one example given in
  the spec). This was applied consistently to every field, including
  non-string items inside `requestedTests` (e.g. `"requestedTests": [1, 2]`),
  since the spec's stated principle is "the JSON... isn't shaped like an
  order."
- **`requestedTests` error reporting:** an empty item and a duplicate can
  both be present in the same array. Both `INVALID_VALUE` (empty item) and
  `DUPLICATE` are reported if both conditions occur, but never more than one
  `DUPLICATE` error, and never more than one `INVALID_VALUE` error even if
  multiple items are blank — matching "one DUPLICATE error is enough."
- **Original casing is preserved** in the `RequestedTests` list returned on
  an accepted order — only `specimenType` and `priority` are normalized, per
  spec.
- **Malformed field name is always `"$"`**, used for every malformed-input
  case (broken JSON, non-object root, null/empty/whitespace input, and any
  field-level type mismatch), not just for broken JSON syntax.

## Limitations / out of scope (per spec)

No database, auth, UI, HTTP API, cloud dependency, external API calls,
queues, load testing, or production logging/validation frameworks are
included — the service is a plain, dependency-free class library.

## AI use

[Describe here how you used AI, if at all — e.g.: "Used Claude to draft the
initial JSON type-checking approach in `TryGetString`/`TryGetStringArray`. I
reviewed and changed the suggested implementation to also reject non-string
items inside `requestedTests` as malformed, since the first draft only
checked the array's own type and would have let `[1, 2]` through as an
empty-after-filtering list instead of flagging it as malformed input." If you
did not use AI, replace this with a decision you worked through yourself,
e.g. how you chose to distinguish `REQUIRED` from `MALFORMED_INPUT` for
missing vs. wrongly-typed fields.]

## Before submitting

- [ ] `dotnet build` passes with no errors
- [ ] `dotnet test` passes, all 6 required groups represented
- [ ] README reflects the actual final code (update the AI-use section above)
- [ ] Repository is accessible to the reviewer
- [ ] Screen recording (with voice-over) walking through design, tests, and a
      live run, pushed alongside the code
