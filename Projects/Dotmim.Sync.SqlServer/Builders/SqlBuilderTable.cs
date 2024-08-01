﻿using Dotmim.Sync.DatabaseStringParsers;
using Dotmim.Sync.Manager;
using Dotmim.Sync.SqlServer.Manager;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dotmim.Sync.SqlServer.Builders
{

    /// <summary>
    /// Sql table builder for Sql Server.
    /// </summary>
    public class SqlBuilderTable
    {
        private SyncTable tableDescription;
        private SqlObjectNames sqlObjectNames;
        private SqlDbMetadata sqlDbMetadata;
        private Dictionary<string, string> createdRelationNames = [];

        /// <inheritdoc cref="SqlBuilderTable"/>
        public SqlBuilderTable(SyncTable tableDescription, SqlObjectNames sqlObjectNames, SqlDbMetadata sqlDbMetadata)
        {
            this.tableDescription = tableDescription;
            this.sqlObjectNames = sqlObjectNames;
            this.sqlDbMetadata = sqlDbMetadata;
        }

        private static string GetRandomString() =>
#if NET6_0_OR_GREATER
            Path.GetRandomFileName().Replace(".", string.Empty, SyncGlobalization.DataSourceStringComparison).ToLowerInvariant();
#else
            Path.GetRandomFileName().Replace(".", string.Empty).ToLowerInvariant();
#endif

        /// <summary>
        /// Ensure the relation name is correct to be created in MySql.
        /// </summary>
        public string NormalizeRelationName(string relation)
        {
            if (this.createdRelationNames.TryGetValue(relation, out var name))
                return name;

            name = relation;

            if (relation.Length > 128)
                name = $"{relation.Substring(0, 110)}_{GetRandomString()}";

            // MySql could have a special character in its relation names
#if NET6_0_OR_GREATER
            name = name.Replace("~", string.Empty, SyncGlobalization.DataSourceStringComparison).Replace("#", string.Empty, SyncGlobalization.DataSourceStringComparison);
#else
            name = name.Replace("~", string.Empty).Replace("#", string.Empty);
#endif

            this.createdRelationNames.Add(relation, name);

            return name;
        }

        /// <summary>
        /// Get the create schema command.
        /// </summary>
        public Task<DbCommand> GetCreateSchemaCommandAsync(DbConnection connection, DbTransaction transaction)
        {

            if (this.sqlObjectNames.TableSchemaName == "dbo")
                return Task.FromResult<DbCommand>(null);

            var command = connection.CreateCommand();

            command.Connection = connection;
            command.Transaction = transaction;
            command.CommandText = $"Create Schema {this.sqlObjectNames.TableSchemaName}";

            return Task.FromResult(command);
        }

        /// <summary>
        /// Get Create table command.
        /// </summary>
        public Task<DbCommand> GetCreateTableCommandAsync(DbConnection connection, DbTransaction transaction)
        {
            var command = this.BuildCreateTableCommand(connection, transaction);
            return Task.FromResult((DbCommand)command);
        }

        /// <summary>
        /// Get Exists table command.
        /// </summary>
        public Task<DbCommand> GetExistsTableCommandAsync(DbConnection connection, DbTransaction transaction)
        {
            var command = connection.CreateCommand();

            command.Connection = connection;
            command.Transaction = transaction;
            command.CommandText = $"IF EXISTS (SELECT t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE t.name = @tableName AND s.name = @schemaName) SELECT 1 ELSE SELECT 0;";

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@tableName";
            parameter.Value = this.sqlObjectNames.TableName;
            command.Parameters.Add(parameter);

            parameter = command.CreateParameter();
            parameter.ParameterName = "@schemaName";
            parameter.Value = this.sqlObjectNames.TableSchemaName;
            command.Parameters.Add(parameter);

            return Task.FromResult(command);
        }

        /// <summary>
        /// Get Exists schema command.
        /// </summary>
        public Task<DbCommand> GetExistsSchemaCommandAsync(DbConnection connection, DbTransaction transaction)
        {
            var command = connection.CreateCommand();

            command.Connection = connection;
            command.Transaction = transaction;
            command.CommandText = "IF EXISTS (SELECT sch.name FROM sys.schemas sch WHERE sch.name = @schemaName) SELECT 1 ELSE SELECT 0";

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@schemaName";
            parameter.Value = this.sqlObjectNames.TableSchemaName;
            command.Parameters.Add(parameter);

            return Task.FromResult(command);
        }

        /// <summary>
        /// Get Drop table command.
        /// </summary>
        public Task<DbCommand> GetDropTableCommandAsync(DbConnection connection, DbTransaction transaction)
        {
            var command = connection.CreateCommand();

            command.Connection = connection;
            command.Transaction = transaction;
            command.CommandText = $"IF EXISTS (SELECT t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE t.name = @tableName AND s.name = @schemaName) " +
                $"BEGIN " +
                $"ALTER TABLE {this.sqlObjectNames.TableQuotedFullName} NOCHECK CONSTRAINT ALL; " +
                $"DROP TABLE {this.sqlObjectNames.TableQuotedFullName}; " +
                $"END";

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@tableName";
            parameter.Value = this.sqlObjectNames.TableName;
            command.Parameters.Add(parameter);

            parameter = command.CreateParameter();
            parameter.ParameterName = "@schemaName";
            parameter.Value = this.sqlObjectNames.TableSchemaName;
            command.Parameters.Add(parameter);

            return Task.FromResult(command);
        }

        /// <summary>
        /// Get Add column command.
        /// </summary>
        public Task<DbCommand> GetAddColumnCommandAsync(string columnName, DbConnection connection, DbTransaction transaction)
        {
            var command = connection.CreateCommand();

            command.Connection = connection;
            command.Transaction = transaction;

            var stringBuilder = new StringBuilder($"ALTER TABLE {this.sqlObjectNames.TableQuotedFullName} WITH NOCHECK ");
            var quotedColumnNameString = new ObjectParser(columnName, SqlObjectNames.LeftQuote, SqlObjectNames.RightQuote).QuotedShortName;

            var column = this.tableDescription.Columns[columnName];
            var columnType = this.sqlDbMetadata.GetCompatibleColumnTypeDeclarationString(column, this.tableDescription.OriginalProvider);

            var identity = string.Empty;

            if (column.IsAutoIncrement)
            {
                var s = column.GetAutoIncrementSeedAndStep();
                identity = $"IDENTITY({s.Seed},{s.Step})";
            }

            var nullString = column.AllowDBNull ? "NULL" : "NOT NULL";

            // if we have a computed column, we should allow null
            if (column.IsReadOnly)
                nullString = "NULL";

            string defaultValue = string.Empty;

            // Ok, not the best solution to know if we have SqlSyncChangeTrackingProvider ...
            if (this.tableDescription.OriginalProvider == SqlSyncProvider.ProviderType ||
                this.tableDescription.OriginalProvider == "SqlSyncChangeTrackingProvider, Dotmim.Sync.SqlServer.SqlSyncChangeTrackingProvider")
            {
                if (!string.IsNullOrEmpty(column.DefaultValue))
                {
                    defaultValue = "DEFAULT " + column.DefaultValue;
                }
            }

            stringBuilder.AppendLine($"ADD {quotedColumnNameString} {columnType} {identity} {nullString} {defaultValue}");

            command.CommandText = stringBuilder.ToString();

            return Task.FromResult(command);
        }

        /// <summary>
        /// Get Drop column command.
        /// </summary>
        public Task<DbCommand> GetDropColumnCommandAsync(string columnName, DbConnection connection, DbTransaction transaction)
        {
            var command = connection.CreateCommand();

            command.Connection = connection;
            command.Transaction = transaction;
            command.CommandText = $"ALTER TABLE {this.sqlObjectNames.TableQuotedFullName} WITH NOCHECK DROP COLUMN {columnName};";

            return Task.FromResult(command);
        }

        /// <summary>
        /// Get Exists column command.
        /// </summary>
        public Task<DbCommand> GetExistsColumnCommandAsync(string columnName, DbConnection connection, DbTransaction transaction)
        {
            var command = connection.CreateCommand();

            command.Connection = connection;
            command.Transaction = transaction;
            command.CommandText = $"IF EXISTS (" +
                $"SELECT col.* " +
                $"FROM sys.columns as col " +
                $"JOIN sys.tables as t on t.object_id = col.object_id " +
                $"JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE t.name = @tableName AND s.name = @schemaName and col.name=@columnName) SELECT 1 ELSE SELECT 0;";

            var parameter = command.CreateParameter();
            parameter.ParameterName = "@tableName";
            parameter.Value = this.sqlObjectNames.TableName;
            command.Parameters.Add(parameter);

            parameter = command.CreateParameter();
            parameter.ParameterName = "@schemaName";
            parameter.Value = this.sqlObjectNames.TableSchemaName;
            command.Parameters.Add(parameter);

            parameter = command.CreateParameter();
            parameter.ParameterName = "@columnName";
            parameter.Value = columnName;
            command.Parameters.Add(parameter);

            return Task.FromResult(command);
        }

        private SqlCommand BuildCreateTableCommand(DbConnection connection, DbTransaction transaction)
        {
            var stringBuilder = new StringBuilder();

            stringBuilder.AppendLine($"CREATE TABLE {this.sqlObjectNames.TableQuotedFullName} (");
            string empty = string.Empty;
            stringBuilder.AppendLine();
            foreach (var column in this.tableDescription.Columns)
            {
                var columnName = new ObjectParser(column.ColumnName, SqlObjectNames.LeftQuote, SqlObjectNames.RightQuote).QuotedShortName;
                var columnType = this.sqlDbMetadata.GetCompatibleColumnTypeDeclarationString(column, this.tableDescription.OriginalProvider);
                var identity = string.Empty;

                if (column.IsAutoIncrement)
                {
                    var s = column.GetAutoIncrementSeedAndStep();
                    identity = $"IDENTITY({s.Seed},{s.Step})";
                }

                var nullString = column.AllowDBNull ? "NULL" : "NOT NULL";

                // if we have a computed column, we should allow null
                if (column.IsReadOnly)
                    nullString = "NULL";

                string defaultValue = string.Empty;

                // Ok, not the best solution to know if we have SqlSyncChangeTrackingProvider ...
                if (this.tableDescription.OriginalProvider == SqlSyncProvider.ProviderType ||
                    this.tableDescription.OriginalProvider == "SqlSyncChangeTrackingProvider, Dotmim.Sync.SqlServer.SqlSyncChangeTrackingProvider")
                {
                    if (!string.IsNullOrEmpty(column.DefaultValue))
                    {
                        defaultValue = "DEFAULT " + column.DefaultValue;
                    }
                }

                stringBuilder.AppendLine($"\t{empty}{columnName} {columnType} {identity} {nullString} {defaultValue}");
                empty = ",";
            }

            stringBuilder.AppendLine(");");

            // Primary Keys
            stringBuilder.AppendLine($"ALTER TABLE {this.sqlObjectNames.TableQuotedFullName} ADD CONSTRAINT [PK_{this.sqlObjectNames.TableNormalizedFullName}] PRIMARY KEY(");
            for (int i = 0; i < this.tableDescription.PrimaryKeys.Count; i++)
            {
                var pkColumn = this.tableDescription.PrimaryKeys[i];

                var quotedColumnName = new ObjectParser(pkColumn, SqlObjectNames.LeftQuote, SqlObjectNames.RightQuote).QuotedShortName;
                stringBuilder.Append(quotedColumnName);

                if (i < this.tableDescription.PrimaryKeys.Count - 1)
                    stringBuilder.Append(", ");
            }

            stringBuilder.AppendLine(");");

            // Foreign Keys
            foreach (var constraint in this.tableDescription.GetRelations())
            {
                var tableName = new TableParser(constraint.GetTable().GetFullName(), SqlObjectNames.LeftQuote, SqlObjectNames.RightQuote).QuotedFullName;
                var parentTableName = new TableParser(constraint.GetParentTable().GetFullName(), SqlObjectNames.LeftQuote, SqlObjectNames.RightQuote).QuotedFullName;
                var relationName = this.NormalizeRelationName(constraint.RelationName);

                stringBuilder.Append("ALTER TABLE ");
                stringBuilder.Append(tableName);
                stringBuilder.AppendLine(" WITH NOCHECK");
                stringBuilder.Append("ADD CONSTRAINT ");
                stringBuilder.AppendLine($"[{relationName}]");
                stringBuilder.Append("FOREIGN KEY (");
                empty = string.Empty;
                foreach (var column in constraint.Keys)
                {
                    var childColumnName = new ObjectParser(column.ColumnName, SqlObjectNames.LeftQuote, SqlObjectNames.RightQuote).QuotedShortName;
                    stringBuilder.Append($"{empty} {childColumnName}");
                    empty = ", ";
                }

                stringBuilder.AppendLine(" )");
                stringBuilder.Append("REFERENCES ");
                stringBuilder.Append(parentTableName).Append(" (");
                empty = string.Empty;
                foreach (var parentdColumn in constraint.ParentKeys)
                {
                    var parentColumnName = new ObjectParser(parentdColumn.ColumnName, SqlObjectNames.LeftQuote, SqlObjectNames.RightQuote).QuotedShortName;
                    stringBuilder.Append($"{empty} {parentColumnName}");
                    empty = ", ";
                }

                stringBuilder.Append(" ) ");
            }

            string createTableCommandString = stringBuilder.ToString();

            var command = new SqlCommand(createTableCommandString, (SqlConnection)connection, (SqlTransaction)transaction);

            return command;
        }

        /// <summary>
        /// Get the primary keys for the current table.
        /// </summary>
        internal async Task<IEnumerable<SyncColumn>> GetPrimaryKeysAsync(DbConnection connection, DbTransaction transaction)
        {
            var syncTableKeys = await SqlManagementUtils.GetPrimaryKeysForTableAsync(this.sqlObjectNames.TableName, this.sqlObjectNames.TableSchemaName,
                (SqlConnection)connection, (SqlTransaction)transaction).ConfigureAwait(false);

            var lstKeys = new List<SyncColumn>();

            foreach (var dmKey in syncTableKeys.Rows)
            {
                var keyColumn = SyncColumn.Create<string>((string)dmKey["columnName"]);
                keyColumn.Ordinal = Convert.ToInt32(dmKey["column_id"]);
                lstKeys.Add(keyColumn);
            }

            return lstKeys;
        }

        /// <summary>
        /// Get relations definition for the current table.
        /// </summary>
        internal async Task<IEnumerable<DbRelationDefinition>> GetRelationsAsync(DbConnection connection, DbTransaction transaction)
        {
            var relations = new List<DbRelationDefinition>();
            var tableRelations = await SqlManagementUtils.GetRelationsForTableAsync((SqlConnection)connection, (SqlTransaction)transaction,
                this.sqlObjectNames.TableName, this.sqlObjectNames.TableSchemaName).ConfigureAwait(false);

            if (tableRelations != null && tableRelations.Rows.Count > 0)
            {
                foreach (var fk in tableRelations.Rows.GroupBy(row =>
                new
                {
                    Name = (string)row["ForeignKey"],
                    TableName = (string)row["TableName"],
                    SchemaName = (string)row["SchemaName"] == "dbo" ? string.Empty : (string)row["SchemaName"],
                    ReferenceTableName = (string)row["ReferenceTableName"],
                    ReferenceSchemaName = (string)row["ReferenceSchemaName"] == "dbo" ? string.Empty : (string)row["ReferenceSchemaName"],
                }))
                {
                    var relationDefinition = new DbRelationDefinition()
                    {
                        ForeignKey = fk.Key.Name,
                        TableName = fk.Key.TableName,
                        SchemaName = fk.Key.SchemaName,
                        ReferenceTableName = fk.Key.ReferenceTableName,
                        ReferenceSchemaName = fk.Key.ReferenceSchemaName,
                    };

                    relationDefinition.Columns.AddRange(fk.Select(dmRow =>
                       new DbRelationColumnDefinition
                       {
                           KeyColumnName = (string)dmRow["ColumnName"],
                           ReferenceColumnName = (string)dmRow["ReferenceColumnName"],
                           Order = (int)dmRow["ForeignKeyOrder"],
                       }));

                    relations.Add(relationDefinition);
                }
            }

            return [.. relations.OrderBy(t => t.ForeignKey)];
        }

        /// <summary>
        /// Get the table columns definition.
        /// </summary>
        internal async Task<IEnumerable<SyncColumn>> GetColumnsAsync(DbConnection connection, DbTransaction transaction)
        {
            var columns = new List<SyncColumn>();

            // Get the columns definition
            var syncTableColumnsList = await SqlManagementUtils.GetColumnsForTableAsync(this.sqlObjectNames.TableName, this.sqlObjectNames.TableSchemaName,
                (SqlConnection)connection, (SqlTransaction)transaction).ConfigureAwait(false);

            foreach (var c in syncTableColumnsList.Rows.OrderBy(r => (int)r["column_id"]))
            {
                var typeName = c["type"].ToString();
                var name = c["name"].ToString();
                var maxLengthLong = Convert.ToInt64(c["max_length"]);

                //// Gets the datastore owner dbType
                // var datastoreDbType = (SqlDbType)sqlDbMetadata.ValidateOwnerDbType(typeName, false, false, maxLengthLong);

                //// once we have the datastore type, we can have the managed type
                // var columnType = sqlDbMetadata.ValidateType(datastoreDbType);
                var sColumn = new SyncColumn(name)
                {
                    OriginalDbType = typeName,
                    Ordinal = (int)c["column_id"],
                    OriginalTypeName = c["type"].ToString(),
                    MaxLength = maxLengthLong > int.MaxValue ? int.MaxValue : (int)maxLengthLong,
                    Precision = (byte)c["precision"],
                    Scale = (byte)c["scale"],
                    AllowDBNull = (bool)c["is_nullable"],
                    IsAutoIncrement = (bool)c["is_identity"],
                    IsUnique = c["is_unique"] != DBNull.Value && (bool)c["is_unique"],
                    IsCompute = (bool)c["is_computed"],
                    DefaultValue = c["defaultvalue"] != DBNull.Value ? c["defaultvalue"].ToString() : null,
                };

                if (sColumn.IsAutoIncrement)
                {
                    sColumn.AutoIncrementSeed = Convert.ToInt64(c["seed"]);
                    sColumn.AutoIncrementStep = Convert.ToInt64(c["step"]);
                }

                sColumn.IsUnicode = sColumn.OriginalTypeName.ToLowerInvariant() switch
                {
                    "nchar" or "nvarchar" => true,
                    _ => false,
                };

                // No unsigned type in SQL Server
                sColumn.IsUnsigned = false;

                columns.Add(sColumn);
            }

            return columns;
        }
    }
}