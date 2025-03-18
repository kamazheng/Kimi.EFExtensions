// ***********************************************************************
// Author           : Kama Zheng
// Created          : 03/17/2025
// ***********************************************************************

using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Kimi.EFExtensions.Auditing;

[Table("AuditTrail", Schema = "Data")]
[Index(nameof(TableName), nameof(AuditOn), nameof(Updated), nameof(Updatedby))]
public class Trail
{
    [Column(nameof(Id), Order = 1)]
    [Key]
    public int Id { get; set; }

    [MaxLength(256)]
    [Column(nameof(Name), Order = 2)]
    [Display(Name = nameof(Name))]
    public string Name { get; set; } = string.Empty;

    [MaxLength(512)]
    [Column(nameof(Description), Order = 3)]
    [Display(Name = nameof(Description))]
    public string? Description { get; set; } = string.Empty;

    [MaxLength(50)]
    public string UserId { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Type { get; set; }

    [MaxLength(100)]
    public string? TableName { get; set; }

    [Precision(3)]
    public DateTime AuditOn { get; set; }

    [MaxLength(-1)]
    public string? OldValues { get; set; }

    [MaxLength(-1)]
    public string? NewValues { get; set; }

    [MaxLength(500)]
    public string? AffectedColumns { get; set; }

    [MaxLength(100)]
    public string? PrimaryKey { get; set; }

    [Column("UPDATEDBY", Order = int.MaxValue - 2)]
    [MaxLength(50)]
    [Display(Name = nameof(Updatedby))]
    public string Updatedby { get; set; } = string.Empty;

    [Column("UPDATED", Order = int.MaxValue - 1)]
    [Precision(3)]
    [Display(Name = nameof(Updated))]
    public DateTime Updated { get; set; }

    [Column("ACTIVE", Order = int.MaxValue)]
    [Display(Name = nameof(Active))]
    public bool Active { get; set; } = true;

}
