// Copyright (c) 2022 Nitro Agility S.r.l.
// SPDX-License-Identifier: Apache-2.0

using System.Numerics;
using System.Text.Json;

namespace Permguard;

/// <summary>
/// The values of a request, checked with the server's rule before either transport sends them
/// (BUG-0306, BUG-0307): an integer past ±2^53 is <c>validation</c>/<c>value_unrepresentable</c>;
/// a value that is not JSON (NaN, an infinity) is <c>validation</c>/<c>payload_malformed</c>.
/// </summary>
internal static class Values
{
    /// <summary>The largest integer both transports carry exactly, the server's bound: 2^53.</summary>
    internal const long Exact = 1L << 53;

    internal static void Check(EvaluateRequest request)
    {
        JsonElement tree;
        try
        {
            tree = JsonSerializer.SerializeToElement(request, Json.Options);
        }
        catch (ArgumentException error)
        {
            throw new Refusal("validation", "payload_malformed", $"the request is not JSON: {error.Message}",
                innerException: error);
        }
        Check(tree);
    }

    internal static void Check(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                var text = value.GetRawText();
                if (text.IndexOfAny(['.', 'e', 'E']) < 0 &&
                    BigInteger.Abs(BigInteger.Parse(text, System.Globalization.CultureInfo.InvariantCulture)) > Exact)
                {
                    throw Unrepresentable(text);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray()) Check(item);
                break;
            case JsonValueKind.Object:
                foreach (var item in value.EnumerateObject()) Check(item.Value);
                break;
        }
    }

    internal static Refusal Unrepresentable(string number) => new("validation", "value_unrepresentable",
        $"{number} is beyond the largest integer both transports carry exactly (2^53): send it as a string");
}
