﻿// ***********************************************************************
// Author           : Kama Zheng
// Created          : 03/17/2025
// ***********************************************************************

namespace Kimi.EFExtensions.Interfaces;

public interface IAuditableEntity
{
    public string CreatedBy { get; set; }
    public DateTime CreatedOn { get; set; }
}
