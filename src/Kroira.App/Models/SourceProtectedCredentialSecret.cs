using System;

namespace Kroira.App.Models
{
    public sealed class SourceProtectedCredentialSecret
    {
        public int Id { get; set; }
        public int SourceProfileId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ProtectedValue { get; set; } = string.Empty;
        public string ProtectionScheme { get; set; } = string.Empty;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
