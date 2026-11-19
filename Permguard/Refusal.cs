// Copyright (c) 2022 Nitro Agility S.r.l.
// SPDX-License-Identifier: Apache-2.0

using Grpc.Core;

namespace Permguard;

/// <summary>A structured PDP error. A deny is a successful response, not a refusal.</summary>
public sealed class Refusal : Exception
{
    /// <summary>
    /// The class of a call that failed in the client, the same on HTTP and gRPC and in every Permguard
    /// SDK: code <see cref="Timeout"/> when the call's timeout passed, <see cref="Unreachable"/> when
    /// the data plane could not be reached. A cancellation of the caller's own token is not a refusal.
    /// </summary>
    public const string Unavailable = "unavailable";
    /// <summary>The code of a call whose timeout passed in the client.</summary>
    public const string Timeout = "timeout";
    /// <summary>The code of a call whose data plane could not be reached.</summary>
    public const string Unreachable = "unreachable";

    internal static Refusal TimedOut(Exception cause) => new(Unavailable, Timeout,
        "the data plane did not answer within the call's timeout",
        grpcStatus: (cause as RpcException)?.StatusCode, innerException: cause);

    internal static Refusal NotReached(Exception cause) => new(Unavailable, Unreachable,
        $"the data plane could not be reached: {cause.Message}",
        grpcStatus: (cause as RpcException)?.StatusCode, innerException: cause);

    public Refusal(string errorClass, string code, string message, int? httpStatus = null,
        StatusCode? grpcStatus = null, Exception? innerException = null)
        : base(code.Length == 0 ? message : $"{code}: {message}", innerException)
    {
        ErrorClass = errorClass;
        Code = code;
        Detail = message;
        HttpStatus = httpStatus;
        GrpcStatus = grpcStatus;
    }

    public string ErrorClass { get; }
    public string Code { get; }
    public string Detail { get; }
    public int? HttpStatus { get; }
    public StatusCode? GrpcStatus { get; }
}
