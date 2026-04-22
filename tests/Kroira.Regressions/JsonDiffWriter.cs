using System.Text;
using System.Text.Json.Nodes;

namespace Kroira.Regressions;

internal static class JsonDiffWriter
{
    public static string CreateDiff(string expectedJson, string actualJson, int maxDifferences = 40)
    {
        var expectedNode = JsonNode.Parse(expectedJson);
        var actualNode = JsonNode.Parse(actualJson);
        var differences = new List<string>();
        Compare("$", expectedNode, actualNode, differences, maxDifferences);

        if (differences.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var difference in differences)
        {
            builder.AppendLine(difference);
        }

        return builder.ToString().TrimEnd();
    }

    private static void Compare(string path, JsonNode? expected, JsonNode? actual, List<string> differences, int maxDifferences)
    {
        if (differences.Count >= maxDifferences)
        {
            return;
        }

        if (expected == null && actual == null)
        {
            return;
        }

        if (expected == null || actual == null)
        {
            differences.Add($"{path}: expected {Format(expected)}, actual {Format(actual)}");
            return;
        }

        if (expected is JsonValue || actual is JsonValue)
        {
            var expectedText = expected.ToJsonString(RegressionJson.Compact);
            var actualText = actual.ToJsonString(RegressionJson.Compact);
            if (!string.Equals(expectedText, actualText, StringComparison.Ordinal))
            {
                differences.Add($"{path}: expected {expectedText}, actual {actualText}");
            }

            return;
        }

        if (expected is JsonObject expectedObject && actual is JsonObject actualObject)
        {
            var keys = expectedObject.Select(pair => pair.Key)
                .Concat(actualObject.Select(pair => pair.Key))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal);

            foreach (var key in keys)
            {
                Compare($"{path}.{key}", expectedObject[key], actualObject[key], differences, maxDifferences);
                if (differences.Count >= maxDifferences)
                {
                    return;
                }
            }

            return;
        }

        if (expected is JsonArray expectedArray && actual is JsonArray actualArray)
        {
            if (expectedArray.Count != actualArray.Count)
            {
                differences.Add($"{path}: expected array length {expectedArray.Count}, actual {actualArray.Count}");
            }

            var count = Math.Min(expectedArray.Count, actualArray.Count);
            for (var index = 0; index < count; index++)
            {
                Compare($"{path}[{index}]", expectedArray[index], actualArray[index], differences, maxDifferences);
                if (differences.Count >= maxDifferences)
                {
                    return;
                }
            }

            return;
        }

        differences.Add($"{path}: node type mismatch");
    }

    private static string Format(JsonNode? node)
    {
        return node?.ToJsonString(RegressionJson.Compact) ?? "null";
    }
}
