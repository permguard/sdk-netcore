// Copyright (c) 2022 Nitro Agility S.r.l.
// SPDX-License-Identifier: Apache-2.0

using Grpc.Core;

namespace Permguard;

/// <summary>A structured PDP error. A deny is a successful response, not a refusal.</summary>
public sealed class Refusal : Exception
{
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
