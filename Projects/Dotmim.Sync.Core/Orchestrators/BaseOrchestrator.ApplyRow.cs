﻿using Dotmim.Sync.Args;
using Dotmim.Sync.Batch;
using Dotmim.Sync.Builders;
using Dotmim.Sync.Enumerations;
using Dotmim.Sync.Serialization;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Dotmim.Sync
{
    public abstract partial class BaseOrchestrator
    {
        /// <summary>
        /// Apply a delete on a row. if forceWrite, force the delete
        /// </summary>
        private async Task<(SyncContext context, bool applied, Exception exception)> InternalApplyDeleteAsync(IScopeInfo scopeInfo, SyncContext context, DbSyncAdapter syncAdapter, SyncRow row, long? lastTimestamp, Guid? senderScopeId, bool forceWrite, DbConnection connection, DbTransaction transaction)
        {
            var (command, _) = await syncAdapter.GetCommandAsync(DbCommandType.DeleteRow, connection, transaction);

            if (command == null) return (context, false, null);

            // Set the parameters value from row
            syncAdapter.SetColumnParametersValues(command, row);

            // Set the special parameters for update
            syncAdapter.AddScopeParametersValues(command, senderScopeId, lastTimestamp, true, forceWrite);

            await this.InterceptAsync(new DbCommandArgs(context, command, connection, transaction)).ConfigureAwait(false);

            Exception exception = null;
            int rowDeletedCount = 0;

            try
            {
                rowDeletedCount = await command.ExecuteNonQueryAsync().ConfigureAwait(false);

                // Check if we have a return value instead
                var syncRowCountParam = DbSyncAdapter.GetParameter(command, "sync_row_count");

                if (syncRowCountParam != null)
                    rowDeletedCount = (int)syncRowCountParam.Value;

                command.Dispose();
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            return (context, rowDeletedCount > 0, exception);
        }

        /// <summary>
        /// Apply a single update in the current datasource. if forceWrite, force the update
        /// </summary>
        private async Task<(SyncContext context, bool applied, Exception exception)> InternalApplyUpdateAsync(IScopeInfo scopeInfo, SyncContext context, DbSyncAdapter syncAdapter, SyncRow row, long? lastTimestamp, Guid? senderScopeId, bool forceWrite, DbConnection connection, DbTransaction transaction)
        {
            var (command, _) = await syncAdapter.GetCommandAsync(DbCommandType.UpdateRow, connection, transaction);

            if (command == null) return (context, false, null);

            // Set the parameters value from row
            syncAdapter.SetColumnParametersValues(command, row);

            // Set the special parameters for update
            syncAdapter.AddScopeParametersValues(command, senderScopeId, lastTimestamp, false, forceWrite);

            await this.InterceptAsync(new DbCommandArgs(context, command, connection, transaction)).ConfigureAwait(false);

            Exception exception = null;
            int rowUpdatedCount = 0;
            try
            {
                rowUpdatedCount = await command.ExecuteNonQueryAsync().ConfigureAwait(false);

                // Check if we have a return value instead
                var syncRowCountParam = DbSyncAdapter.GetParameter(command, "sync_row_count");

                if (syncRowCountParam != null)
                    rowUpdatedCount = (int)syncRowCountParam.Value;

                command.Dispose();
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            return (context, rowUpdatedCount > 0, exception);
        }
    }
}
