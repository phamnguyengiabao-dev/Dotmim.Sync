﻿using Dotmim.Sync.Enumerations;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Text;
using System.Threading.Tasks;

namespace Dotmim.Sync
{

    /// <summary>
    /// Event args generated when a connection is opened
    /// </summary>
    public class ConnectionOpenedArgs : ProgressArgs
    {
        public ConnectionOpenedArgs(SyncContext context, DbConnection connection)
            : base(context, connection)
        {
        }
        public override SyncProgressLevel ProgressLevel => SyncProgressLevel.Trace;

        public override string Source => Connection.Database;
        public override string Message => $"[{Connection.Database}] Connection Opened.";

        public override int EventId => SyncEventsId.ConnectionOpen.Id;
    }

    /// <summary>
    /// Event args generated when trying to reconnect
    /// </summary>
    public class ReConnectArgs : ProgressArgs
    {
        public ReConnectArgs(SyncContext context, DbConnection connection, Exception handledException, int retry, TimeSpan waitingTimeSpan)
            : base(context, connection)
        {
            this.HandledException = handledException;
            this.Retry = retry;
            this.WaitingTimeSpan = waitingTimeSpan;
        }

        public override SyncProgressLevel ProgressLevel => SyncProgressLevel.Trace;

        public override string Source => Connection.Database;
        public override string Message => $"[{Connection.Database}] Trying to Reconnect...";

        /// <summary>
        /// Gets the handled exception
        /// </summary>
        public Exception HandledException { get; }

        /// <summary>
        /// Gets the retry count
        /// </summary>
        public int Retry { get; }

        /// <summary>
        /// Gets the waiting timespan duration
        /// </summary>
        public TimeSpan WaitingTimeSpan { get; }
        public override int EventId => SyncEventsId.ReConnect.Id;
    }

    /// <summary>
    /// Event args generated when a connection is closed 
    /// </summary>
    public class ConnectionClosedArgs : ProgressArgs
    {
        public ConnectionClosedArgs(SyncContext context, DbConnection connection)
            : base(context, connection)
        {
        }
        public override SyncProgressLevel ProgressLevel => SyncProgressLevel.Trace;

        public override string Source => Connection.Database;
        public override string Message => $"[{Connection.Database}] Connection Closed.";

        public override int EventId => SyncEventsId.ConnectionClose.Id;
    }

    /// <summary>
    /// Event args generated when a transaction is opened
    /// </summary>
    public class TransactionOpenedArgs : ProgressArgs
    {
        public TransactionOpenedArgs(SyncContext context, DbConnection connection, DbTransaction transaction)
            : base(context, connection, transaction)
        {
        }

        public override string Source => Connection.Database;
        public override string Message => $"[{Connection.Database}] Transaction Opened.";
        public override SyncProgressLevel ProgressLevel => SyncProgressLevel.Trace;
        public override int EventId => SyncEventsId.TransactionOpen.Id;
    }

    /// <summary>
    /// Event args generated when a transaction is commit
    /// </summary>
    public class TransactionCommitArgs : ProgressArgs
    {
        public TransactionCommitArgs(SyncContext context, DbConnection connection, DbTransaction transaction)
            : base(context, connection, transaction)
        {
        }
        public override SyncProgressLevel ProgressLevel => SyncProgressLevel.Trace;
        public override string Source => Connection.Database;
        public override string Message => $"[{Connection.Database}] Transaction Commited.";

        public override int EventId => SyncEventsId.TransactionCommit.Id;
    }

    /// <summary>
    /// Event args generated during BeginSession stage
    /// </summary>
    public class SessionBeginArgs : ProgressArgs
    {
        public SessionBeginArgs(SyncContext context, DbConnection connection)
            : base(context, connection, null)
        {
        }
        public override SyncProgressLevel ProgressLevel => SyncProgressLevel.Information;
        public override string Source => Connection.Database;
        public override string Message => $"[{Connection.Database}] Session Begins. Id:{Context.SessionId}. Scope name:{Context.ScopeName}.";

        public override int EventId => SyncEventsId.SessionBegin.Id;
    }

    /// <summary>
    /// Event args generated during EndSession stage
    /// </summary>
    public class SessionEndArgs : ProgressArgs
    {
        /// <summary>
        /// Gets the sync result
        /// </summary>
        public SyncResult SyncResult { get; }

        public SessionEndArgs(SyncContext context, SyncResult syncResult, DbConnection connection)
            : base(context, connection, null)
        {
            SyncResult = syncResult;
        }
        public override SyncProgressLevel ProgressLevel => SyncProgressLevel.Information;
        public override string Source => Connection.Database;
        public override string Message => $"[{Connection.Database}] Session Ends. Id:{Context.SessionId}. Scope name:{Context.ScopeName}.";
        public override int EventId => SyncEventsId.SessionEnd.Id;
    }

    /// <summary>
    /// Raised as an argument when an apply is failing. Waiting from user for the conflict resolution
    /// </summary>
    public class ApplyChangesFailedArgs : ProgressArgs
    {
        ConflictResolution resolution;

        /// <summary>
        /// Gets or Sets the action to be taken when resolving the conflict. 
        /// If you choose MergeRow, you have to fill the FinalRow property
        /// </summary>
        public ConflictResolution Resolution
        {
            get => this.resolution;
            set
            {
                if (this.resolution != value)
                {
                    this.resolution = value;

                    if (this.resolution == ConflictResolution.MergeRow)
                    {
                        var conflict = this.GetSyncConflictAsync().GetAwaiter().GetResult();
                        var finalRowArray = conflict.RemoteRow.ToArray();
                        var finalTable = conflict.RemoteRow.SchemaTable.Clone();
                        var finalSet = conflict.RemoteRow.SchemaTable.Schema.Clone(false);
                        finalSet.Tables.Add(finalTable);
                        this.FinalRow = new SyncRow(conflict.RemoteRow.SchemaTable, finalRowArray);
                        finalTable.Rows.Add(this.FinalRow);
                    }
                    else if (this.FinalRow != null)
                    {
                        var finalSet = this.FinalRow.SchemaTable.Schema;
                        this.FinalRow.Clear();
                        finalSet.Clear();
                        finalSet.Dispose();
                    }
                }
            }
        }

        public override SyncProgressLevel ProgressLevel => SyncProgressLevel.Debug;
        /// <summary>
        /// Gets or Sets the scope id who will be marked as winner
        /// </summary>
        public Guid? SenderScopeId { get; set; }

        /// <summary>
        /// If we have a merge action, the final row represents the merged row
        /// </summary>
        public SyncRow FinalRow { get; set; }


        private BaseOrchestrator orchestrator;
        private DbSyncAdapter syncAdapter;
        private readonly SyncRow conflictRow;
        private SyncTable schemaChangesTable;

        // used only internally
        internal SyncConflict conflict;

        public async Task<SyncConflict> GetSyncConflictAsync()
        {
            var (_, localRow) = await orchestrator.InternalGetConflictRowAsync(Context, syncAdapter, conflictRow, schemaChangesTable, this.Connection, this.Transaction).ConfigureAwait(false);

            var conflict = orchestrator.InternalGetConflict(conflictRow, localRow);

            this.conflict = conflict;
            return conflict;
        }

        public ApplyChangesFailedArgs(SyncContext context, BaseOrchestrator orchestrator, DbSyncAdapter syncAdapter, SyncRow conflictRow, SyncTable schemaChangesTable, ConflictResolution action, Guid? senderScopeId, DbConnection connection, DbTransaction transaction)
            : base(context, connection, transaction)
        {
            this.orchestrator = orchestrator;
            this.syncAdapter = syncAdapter;
            this.conflictRow = conflictRow;
            this.schemaChangesTable = schemaChangesTable;
            this.resolution = action;
            this.SenderScopeId = senderScopeId;
        }
        public override string Source => Connection.Database;
        public override string Message => $"Conflict {conflictRow}.";
        public override int EventId => SyncEventsId.ApplyChangesFailed.Id;

    }


    public static partial class InterceptorsExtensions
    {
        /// <summary>
        /// Intercept the provider action whenever a connection is opened
        /// </summary>
        public static Guid OnConnectionOpen(this BaseOrchestrator orchestrator, Action<ConnectionOpenedArgs> action)
            => orchestrator.AddInterceptor(action);
        /// <summary>
        /// Intercept the provider action whenever a connection is opened
        /// </summary>
        public static Guid OnConnectionOpen(this BaseOrchestrator orchestrator, Func<ConnectionOpenedArgs, Task> action)
            => orchestrator.AddInterceptor(action);

        /// <summary>
        /// Occurs when trying to reconnect to a database
        /// </summary>
        public static Guid OnReConnect(this BaseOrchestrator orchestrator, Action<ReConnectArgs> action)
            => orchestrator.AddInterceptor(action);
        /// <summary>
        /// Occurs when trying to reconnect to a database
        /// </summary>
        public static Guid OnReConnect(this BaseOrchestrator orchestrator, Func<ReConnectArgs, Task> action)
            => orchestrator.AddInterceptor(action);

        /// <summary>
        /// Intercept the provider action whenever a transaction is opened
        /// </summary>
        public static Guid OnTransactionOpen(this BaseOrchestrator orchestrator, Action<TransactionOpenedArgs> action)
            => orchestrator.AddInterceptor(action);
        /// <summary>
        /// Intercept the provider action whenever a transaction is opened
        /// </summary>
        public static Guid OnTransactionOpen(this BaseOrchestrator orchestrator, Func<TransactionOpenedArgs, Task> action)
            => orchestrator.AddInterceptor(action);

        /// <summary>
        /// Intercept the provider action whenever a connection is closed
        /// </summary>
        public static Guid OnConnectionClose(this BaseOrchestrator orchestrator, Action<ConnectionClosedArgs> action)
            => orchestrator.AddInterceptor(action);
        /// <summary>
        /// Intercept the provider action whenever a connection is closed
        /// </summary>
        public static Guid OnConnectionClose(this BaseOrchestrator orchestrator, Func<ConnectionClosedArgs, Task> action)
            => orchestrator.AddInterceptor(action);

        /// <summary>
        /// Intercept the provider action whenever a transaction is commit
        /// </summary>
        public static Guid OnTransactionCommit(this BaseOrchestrator orchestrator, Action<TransactionCommitArgs> action)
            => orchestrator.AddInterceptor(action);
        /// <summary>
        /// Intercept the provider action whenever a transaction is commit
        /// </summary>
        public static Guid OnTransactionCommit(this BaseOrchestrator orchestrator, Func<TransactionCommitArgs, Task> action)
            => orchestrator.AddInterceptor(action);

        /// <summary>
        /// Intercept the provider action when session begin is called
        /// </summary>
        public static Guid OnSessionBegin(this BaseOrchestrator orchestrator, Action<SessionBeginArgs> action)
            => orchestrator.AddInterceptor(action);
        /// <summary>
        /// Intercept the provider action when session begin is called
        /// </summary>
        public static Guid OnSessionBegin(this BaseOrchestrator orchestrator, Func<SessionBeginArgs, Task> action)
            => orchestrator.AddInterceptor(action);

        /// <summary>
        /// Intercept the provider action when session end is called
        /// </summary>
        public static Guid OnSessionEnd(this BaseOrchestrator orchestrator, Action<SessionEndArgs> action)
            => orchestrator.AddInterceptor(action);
        /// <summary>
        /// Intercept the provider action when session end is called
        /// </summary>
        public static Guid OnSessionEnd(this BaseOrchestrator orchestrator, Func<SessionEndArgs, Task> action)
            => orchestrator.AddInterceptor(action);

        /// <summary>
        /// Intercept the provider when an apply change is failing
        /// </summary>
        public static Guid OnApplyChangesFailed(this BaseOrchestrator orchestrator, Action<ApplyChangesFailedArgs> action)
            => orchestrator.AddInterceptor(action);
        /// <summary>
        /// Intercept the provider when an apply change is failing
        /// </summary>
        public static Guid OnApplyChangesFailed(this BaseOrchestrator orchestrator, Func<ApplyChangesFailedArgs, Task> action)
            => orchestrator.AddInterceptor(action);

    }

    public static partial class SyncEventsId
    {
        public static EventId ConnectionOpen => CreateEventId(9000, nameof(ConnectionOpen));
        public static EventId ConnectionClose => CreateEventId(9050, nameof(ConnectionClose));
        public static EventId ReConnect => CreateEventId(9010, nameof(ReConnect));
        public static EventId TransactionOpen => CreateEventId(9100, nameof(TransactionOpen));
        public static EventId TransactionCommit => CreateEventId(9150, nameof(TransactionCommit));

        public static EventId SessionBegin => CreateEventId(100, nameof(SessionBegin));
        public static EventId SessionEnd => CreateEventId(200, nameof(SessionEnd));
        public static EventId ApplyChangesFailed => CreateEventId(300, nameof(ApplyChangesFailed));
    }
}
