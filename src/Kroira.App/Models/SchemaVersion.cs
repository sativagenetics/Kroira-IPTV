using System;

namespace Kroira.App.Models
{
    public class SchemaVersion
    {
        public int Id { get; set; }
        public int VersionNumber { get; set; }
        public DateTime AppliedAt { get; set; }
        public bool IsValidated { get; set; }
    }
}
