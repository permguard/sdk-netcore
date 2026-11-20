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
    /// <summary>Asks for a signed response: base64url without padding, 16 to 64 bytes.</summary>
    public string? RequestNonce { get; init; }
}

public sealed record Reason(string Code, string Message);

public sealed record DecisionContext
{
    public string? Id { get; init; }
    public Reason? ReasonAdmin { get; init; }
    public Reason? ReasonUser { get; init; }
    public IReadOnlyList<string>? Policies { get; init; }
    public IReadOnlyList<string>? AbsentInputs { get; init; }
    /// <summary>How the decision was recorded, when the PDP records evidence.</summary>
    public Evidence? Evidence { get; init; }
}

/// <summary>Where a recorded decision sits in its host's evidence stream.</summary>
public sealed record EvidencePosition(string HostId, string Run, string Sequence);

/// <summary>The evidence record of a decision: <c>RecordId</c> and <c>Position</c> once recorded.</summary>
public sealed record Evidence(string Mode, string Status)
{
    public string? RecordId { get; init; }
    public EvidencePosition? Position { get; init; }
}

/// <summary>A signed response: the flattened JWS parts over the decision.</summary>
public sealed record Attestation(string Protected, string Payload, string Signature);

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
    /// <summary>Present when the request carried a nonce and the PDP signed the response.</summary>
    public Attestation? Attestation { get; init; }
}

public sealed record Endpoints(string Evaluation, string Evaluations);

public sealed record StoreScope(string In, string Zone, string Ledger, string Profile);

public sealed record Configuration(
    string Interface,
    string Pdp,
    Endpoints Endpoints,
    IReadOnlyList<string> Capabilities,
    StoreScope StoreScope)
{
    public string Version { get; init; } = "";
    public string Component { get; init; } = "";
    public string State { get; init; } = "";
    public string Stage { get; init; } = "";
    public string? Sunset { get; init; }
    public IReadOnlyDictionary<string, ulong> Limits { get; init; } = new Dictionary<string, ulong>();
    public ResponseSigning ResponseSigning { get; init; } = new("");
}

/// <summary>Whether the PDP signs a response a request asks it to sign.</summary>
public sealed record ResponseSigning(string State)
{
    public uint? ValiditySeconds { get; init; }
    public uint? ClockSkewSeconds { get; init; }
}
