﻿using Dotmim.Sync.Builders;
using Dotmim.Sync.Manager;
using System.Data.Common;
#if NET6_0 || NET8_0
using MySqlConnector;
#elif NETSTANDARD
using MySql.Data.MySqlClient;
#endif
using Dotmim.Sync.Enumerations;
using Dotmim.Sync.MySql.Builders;
using System;

namespace Dotmim.Sync.MySql
{

    public class MySqlSyncProvider : CoreProvider
    {
        private static string providerType;
        private DbMetadata dbMetadata;
        private MySqlConnectionStringBuilder builder;

        public override string GetProviderTypeName() => ProviderType;

        public override string ConnectionString
        {
            get => this.builder == null || string.IsNullOrEmpty(this.builder.ConnectionString) ? null : this.builder.ConnectionString;
            set
            {
                this.builder = string.IsNullOrEmpty(value) ? null : new MySqlConnectionStringBuilder(value);

                // Set the default behavior to use Found rows and not Affected rows !
                this.builder.UseAffectedRows = false;
                this.builder.AllowUserVariables = true;
            }
        }

        public MySqlSyncProvider(string connectionString)
            : base() => this.ConnectionString = connectionString;

        public MySqlSyncProvider(MySqlConnectionStringBuilder builder)
            : base()
        {
            if (builder == null || string.IsNullOrEmpty(builder.ConnectionString))
                throw new Exception("You have to provide parameters to the MySql builder to be able to construct a valid connection string.");

            this.builder = builder;

            // Set the default behavior to use Found rows and not Affected rows !
            builder.UseAffectedRows = false;
            builder.AllowUserVariables = true;
        }

        public static string ProviderType
        {
            get
            {
                if (!string.IsNullOrEmpty(providerType))
                    return providerType;

                var type = typeof(MySqlSyncProvider);
                providerType = $"{type.Name}, {type}";

                return providerType;
            }
        }

        private static string shortProviderType;

        public override string GetShortProviderTypeName() => ShortProviderType;

        public static string ShortProviderType
        {
            get
            {
                if (!string.IsNullOrEmpty(shortProviderType))
                    return shortProviderType;

                var type = typeof(MySqlSyncProvider);
                shortProviderType = type.Name;

                return shortProviderType;
            }
        }

        public override ConstraintsLevelAction ConstraintsLevelAction => ConstraintsLevelAction.OnTableLevel;

        /// <summary>
        /// Gets a value indicating whether mySql can be a server side provider.
        /// </summary>
        public override bool CanBeServerProvider => true;

        /// <summary>
        /// Gets or Sets the MySql Metadata object, provided to validate the MySql Columns issued from MySql.
        /// </summary>
        /// <summary>
        /// Gets or sets the Metadata object which parse Sql server types.
        /// </summary>
        public override DbMetadata GetMetadata()
        {
            this.dbMetadata ??= new MySqlDbMetadata();
            return this.dbMetadata;
        }

        public MySqlSyncProvider()
            : base()
        {
        }

        /// <summary>
        /// Gets a chance to make a retry if the error is a transient error.
        /// </summary>
        public override bool ShouldRetryOn(Exception exception)
        {
            Exception ex = exception;
            while (ex != null)
            {
                if (ex is MySqlException mySqlException)
                    return MySqlTransientExceptionDetector.ShouldRetryOn(mySqlException);
                else
                    ex = ex.InnerException;
            }

            return false;
        }

        public override string GetDatabaseName()
        {
            if (this.builder != null && !string.IsNullOrEmpty(this.builder.Database))
                return this.builder.Database;

            return string.Empty;
        }

        public override void EnsureSyncException(SyncException syncException)
        {
            if (this.builder != null && !string.IsNullOrEmpty(this.builder.ConnectionString))
            {
                syncException.DataSource = this.builder.Server;
                syncException.InitialCatalog = this.builder.Database;
            }

            if (syncException.InnerException is not MySqlException mySqlException)
                return;

            syncException.Number = mySqlException.Number;

            return;
        }

        public override DbConnection CreateConnection() => new MySqlConnection(this.ConnectionString);

        public override DbTableBuilder GetTableBuilder(SyncTable tableDescription, ParserName tableName, ParserName trackingTableName, SyncSetup setup, string scopeName)
            => new MySqlTableBuilder(tableDescription, tableName, trackingTableName, setup, scopeName);

        public override DbSyncAdapter GetSyncAdapter(SyncTable tableDescription, ParserName tableName, ParserName trackingTableName, SyncSetup setup, string scopeName)
            => new MySqlSyncAdapter(tableDescription, tableName, trackingTableName, setup, scopeName);

        public override DbScopeBuilder GetScopeBuilder(string scopeInfoTableName) => new MySqlScopeInfoBuilder(scopeInfoTableName);

        public override DbDatabaseBuilder GetDatabaseBuilder() => new MySqlDatabaseBuilder();

        public override (ParserName TableName, ParserName TrackingName) GetParsers(SyncTable tableDescription, SyncSetup setup)
        {
            string tableAndPrefixName = tableDescription.TableName;

            var originalTableName = ParserName.Parse(tableDescription, "`");

            var pref = setup.TrackingTablesPrefix != null ? setup.TrackingTablesPrefix : string.Empty;
            var suf = setup.TrackingTablesSuffix != null ? setup.TrackingTablesSuffix : string.Empty;

            // be sure, at least, we have a suffix if we have empty values.
            // othewise, we have the same name for both table and tracking table
            if (string.IsNullOrEmpty(pref) && string.IsNullOrEmpty(suf))
                suf = "_tracking";

            var trackingTableName = ParserName.Parse($"{pref}{tableAndPrefixName}{suf}", "`");

            return (originalTableName, trackingTableName);
        }
    }
}