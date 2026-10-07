// Copyright (c) 2022 Nitro Agility S.r.l.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using Google.Protobuf.WellKnownTypes;
using Proto = Permguard.Internal.Grpc.V1;

namespace Permguard;

internal static class GrpcMapper
{
    private const long MaximumExactInteger = 9_007_199_254_740_991;

    internal static Proto.EvaluateRequest ToProto(EvaluateRequest source)
    {
        var result = new Proto.EvaluateRequest
        {
            Zone = source.Zone,
            Ledger = source.Ledger,
            Profile = source.Profile ?? "",
            Subject = ToProto(source.Subject),
            Resource = ToProto(source.Resource),
            Action = ToProto(source.Action),
            Context = ToStruct(source.Context),
            Principal = ToProto(source.Principal),
            EvaluationsSemantic = ToProto(source.Options?.EvaluationsSemantic ?? EvaluationsSemantic.Unspecified),
            RequestId = source.RequestId ?? "",
        };
        AddInputs(result.PartitionInputs, source.PartitionInputs);
        if (source.Evaluations is not null) result.Evaluations.Add(source.Evaluations.Select(ToProto));
        return result;
    }

    private static Proto.Evaluation ToProto(Evaluation source)
    {
        var result = new Proto.Evaluation
        {
            Subject = ToProto(source.Subject),
            Resource = ToProto(source.Resource),
            Action = ToProto(source.Action),
            Context = ToStruct(source.Context),
            RequestId = source.RequestId ?? "",
        };
        if (source.PartitionInputs is not null)
        {
            result.PartitionInputs = new Proto.PartitionInputs();
            AddInputs(result.PartitionInputs.Inputs, source.PartitionInputs);
        }
        return result;
    }

    private static Proto.Entity? ToProto(Entity? source) => source is null ? null : new Proto.Entity
    {
        Type = source.Type,
        Id = source.Id,
        Properties = ToStruct(source.Properties),
    };

    private static Proto.Action? ToProto(Action? source) => source is null ? null : new Proto.Action
    {
        Name = source.Name,
        Properties = ToStruct(source.Properties),
    };

    private static void AddInputs(
        IDictionary<string, Proto.PartitionInput> target,
        IReadOnlyDictionary<string, PartitionInput>? source)
    {
        if (source is null) return;
        foreach (var (name, input) in source)
        {
            target.Add(name, new Proto.PartitionInput
            {
                Type = input.Type,
                Data = input.Data is null ? null : ToValue(JsonSerializer.SerializeToElement(input.Data, Json.Options)),
            });
        }
    }

    private static Struct? ToStruct(IReadOnlyDictionary<string, object?>? source)
    {
        if (source is null) return null;
        var result = new Struct();
        foreach (var (name, item) in source)
            result.Fields.Add(name, ToValue(JsonSerializer.SerializeToElement(item, Json.Options)));
        return result;
    }

    private static Value ToValue(JsonElement source) => source.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => Value.ForNull(),
        JsonValueKind.String => Value.ForString(source.GetString()!),
        JsonValueKind.True => Value.ForBool(true),
        JsonValueKind.False => Value.ForBool(false),
        JsonValueKind.Number => Number(source),
        JsonValueKind.Array => new Value
        {
            ListValue = new ListValue { Values = { source.EnumerateArray().Select(ToValue) } },
        },
        JsonValueKind.Object => new Value
        {
            StructValue = new Struct
            {
                Fields = { source.EnumerateObject().ToDictionary(item => item.Name, item => ToValue(item.Value)) },
            },
        },
        _ => throw new ArgumentException($"Unsupported JSON value kind: {source.ValueKind}"),
    };

    private static Value Number(JsonElement source)
    {
        if (source.TryGetInt64(out var integer) &&
            (integer > MaximumExactInteger || integer < -MaximumExactInteger))
            throw new ArgumentOutOfRangeException(nameof(source),
                $"Integer {integer} is not exactly representable by protobuf Value.");

        var number = source.GetDouble();
        if (double.IsNaN(number) || double.IsInfinity(number))
            throw new ArgumentOutOfRangeException(nameof(source), "Number is not representable by protobuf Value.");
        return Value.ForNumber(number);
    }

    private static Proto.EvaluationsSemantic ToProto(EvaluationsSemantic semantic) => semantic switch
    {
        EvaluationsSemantic.Unspecified => Proto.EvaluationsSemantic.Unspecified,
        EvaluationsSemantic.ExecuteAll => Proto.EvaluationsSemantic.ExecuteAll,
        EvaluationsSemantic.DenyOnFirstDeny => Proto.EvaluationsSemantic.DenyOnFirstDeny,
        EvaluationsSemantic.PermitOnFirstPermit => Proto.EvaluationsSemantic.PermitOnFirstPermit,
        _ => throw new ArgumentOutOfRangeException(nameof(semantic), semantic, "Unknown evaluations semantic."),
    };

    internal static EvaluateResponse FromProto(Proto.EvaluateResponse source) => new()
    {
        Decision = source.Decision,
        RequestId = EmptyToNull(source.RequestId),
        Context = FromProto(source.Context),
        Evaluations = source.Evaluations.Select(FromProto).ToArray(),
    };

    private static DecisionResult FromProto(Proto.Decision source) => new()
    {
        Decision = source.Decision_,
        RequestId = EmptyToNull(source.RequestId),
        Context = FromProto(source.Context),
    };

    private static DecisionContext? FromProto(Proto.DecisionContext? source) => source is null ? null : new DecisionContext
    {
        Id = EmptyToNull(source.Id),
        ReasonAdmin = FromProto(source.ReasonAdmin),
        ReasonUser = FromProto(source.ReasonUser),
        Policies = source.Policies.ToArray(),
        AbsentInputs = source.AbsentInputs.ToArray(),
    };

    private static Reason? FromProto(Proto.Reason? source) => source is null
        ? null
        : new Reason(source.Code, source.Message);

    internal static Configuration FromProto(Proto.GetConfigurationResponse source) => new(
        source.Interface,
        source.Pdp,
        new Endpoints(source.Endpoints?.Evaluation ?? "", source.Endpoints?.Evaluations ?? ""),
        source.Capabilities.ToArray(),
        new StoreScope(
            source.StoreScope?.In ?? "",
            source.StoreScope?.Zone ?? "",
            source.StoreScope?.Ledger ?? "",
            source.StoreScope?.Profile ?? ""));

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;
}
