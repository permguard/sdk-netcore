// Copyright (c) 2022 Nitro Agility S.r.l.
// SPDX-License-Identifier: Apache-2.0

namespace Permguard;

public enum EvaluationsSemantic
{
    Unspecified,
    ExecuteAll,
    DenyOnFirstDeny,
    PermitOnFirstPermit,
}

public sealed record Entity(string Type, string Id)
{
    public IReadOnlyDictionary<string, object?>? Properties { get; init; }
}

public sealed record Action(string Name)
{
    public IReadOnlyDictionary<string, object?>? Properties { get; init; }
}

public sealed record PartitionInput(string Type, object? Data = null);

public sealed record Evaluation
{
    public Entity? Subject { get; init; }
    public Entity? Resource { get; init; }
    public Action? Action { get; init; }
    public IReadOnlyDictionary<string, object?>? Context { get; init; }
    public IReadOnlyDictionary<string, PartitionInput>? PartitionInputs { get; init; }
    public string? RequestId { get; init; }
}

public sealed record EvaluationOptions
{
    public EvaluationsSemantic EvaluationsSemantic { get; init; }
}

public sealed record EvaluateRequest(string Zone, string Ledger)
{
    public string? Profile { get; init; }
    public Entity? Subject { get; init; }
    public Entity? Resource { get; init; }
    public Action? Action { get; init; }
    public IReadOnlyDictionary<string, object?>? Context { get; init; }
    public Entity? Principal { get; init; }
    public IReadOnlyDictionary<string, PartitionInput>? PartitionInputs { get; init; }
    public IReadOnlyList<Evaluation>? Evaluations { get; init; }
    public EvaluationOptions? Options { get; init; }
    public string? RequestId { get; init; }
}

public sealed record Reason(string Code, string Message);

public sealed record DecisionContext
{
    public string? Id { get; init; }
    public Reason? ReasonAdmin { get; init; }
    public Reason? ReasonUser { get; init; }
    public IReadOnlyList<string>? Policies { get; init; }
    public IReadOnlyList<string>? AbsentInputs { get; init; }
}

public sealed record DecisionResult
{
    public bool Decision { get; init; }
    public string? RequestId { get; init; }
    public DecisionContext? Context { get; init; }
}

public sealed record EvaluateResponse
{
    public bool Decision { get; init; }
    public string? RequestId { get; init; }
    public DecisionContext? Context { get; init; }
    public IReadOnlyList<DecisionResult>? Evaluations { get; init; }
}

public sealed record Endpoints(string Evaluation, string Evaluations);

public sealed record StoreScope(string In, string Zone, string Ledger, string Profile);

public sealed record Configuration(
    string Interface,
    string Pdp,
    Endpoints Endpoints,
    IReadOnlyList<string> Capabilities,
    StoreScope StoreScope);
