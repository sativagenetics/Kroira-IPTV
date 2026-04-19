#nullable enable
using System;

namespace Kroira.App.Models
{
    public class AppProfile
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsKidsProfile { get; set; }
        public DateTime CreatedAtUtc { get; set; }
    }
}
