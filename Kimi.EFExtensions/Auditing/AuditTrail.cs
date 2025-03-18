// ***********************************************************************
// Author           : Kama Zheng
// Created          : 03/17/2025
// ***********************************************************************

using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;

namespace Kimi.EFExtensions.Auditing;

public class AuditTrail(EntityEntry entry)
{
    public EntityEntry Entry { get; } = entry;
    public string UserId { get; set; } = string.Empty;
    public string? TableName { get; set; }
    public Dictionary<string, object?> KeyValues { get; } = [];
    public Dictionary<string, object?> OldValues { get; } = [];
    public Dictionary<string, object?> NewValues { get; } = [];
    public List<PropertyEntry> TemporaryProperties { get; } = [];
    public TrailType TrailType { get; set; }
    public List<string> ChangedColumns { get; } = [];
    public bool HasTemporaryProperties => TemporaryProperties.Count > 0;

    public Trail ToAuditTrail() =>
        new()
        {
            UserId = UserId,
            Type = TrailType.ToString(),
            TableName = TableName,
            AuditOn = DateTime.UtcNow,
            PrimaryKey = JsonSerializer.Serialize(KeyValues),
            OldValues = OldValues.Count == 0 ? null : JsonSerializer.Serialize(OldValues),
            NewValues = NewValues.Count == 0 ? null : JsonSerializer.Serialize(NewValues),
            AffectedColumns = ChangedColumns.Count == 0 ? null : JsonSerializer.Serialize(ChangedColumns),
            Updated = DateTime.UtcNow,
            Updatedby = UserId,
            Active = true
        };
}
