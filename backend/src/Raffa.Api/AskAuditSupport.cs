using System.Security.Cryptography;
using System.Text;

namespace Raffa.Api;

/// <summary>The <c>resource_type</c> of every Ask audit row (<c>chat.answered</c>, <c>chat.web_researched</c>,
/// ...). The value is read by the AI evaluation harness: never change it.</summary>
internal static class AskAuditResourceType
{
    public const string Value = "ask_raffa_v2";
}

/// <summary>The hash an Ask audit row carries instead of a query: SHA-256, upper-case hex, unsalted.</summary>
internal static class QueryHasher
{
    public static string ComputeHash(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
}
