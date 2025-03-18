// ***********************************************************************
// Author           : Kama Zheng
// Created          : 01/13/2025
// ***********************************************************************

namespace Kimi.EFExtensions
{
    public interface ISoftDeleteEntity
    {
        #region Properties

        /// <summary>
        /// Gets or sets a value indicating whether Active
        /// </summary>
        bool Active { get; set; }

        /// <summary>
        /// Gets or sets the Updated
        /// </summary>
        DateTime Updated { get; set; }

        /// <summary>
        /// Gets or sets the Updatedby
        /// </summary>
        string Updatedby { get; set; }

        #endregion
    }
}