// Copyright (c) 2022 Nitro Agility S.r.l.
// SPDX-License-Identifier: Apache-2.0

namespace Permguard;

internal interface ITransport : IAsyncDisposable
{
    Task<EvaluateResponse> EvaluateAsync(
        EvaluateRequest request, bool many, DateTime deadline, CancellationToken cancellationToken);

    Task<Configuration> GetConfigurationAsync(DateTime deadline, CancellationToken cancellationToken);
}
