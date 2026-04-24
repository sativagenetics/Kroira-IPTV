#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;

namespace Kroira.App.Services
{
    public static class EpgMappingDecisionIdentity
    {
        public static string ComputeStreamUrlHash(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
