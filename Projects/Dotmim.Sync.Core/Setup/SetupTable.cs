﻿using Dotmim.Sync.Builders;
using Dotmim.Sync.Enumerations;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Dotmim.Sync
{
    /// <summary>
    /// Represents a table to be synchronized.
    /// </summary>
    [DataContract(Name = "st"), Serializable]
    public class SetupTable : SyncNamedItem<SetupTable>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SetupTable"/> class.
        /// public ctor for serialization purpose.
        /// </summary>
        public SetupTable()
        {
        }

        /// <summary>
        /// Gets or Sets the table name.
        /// </summary>
        [DataMember(Name = "tn", IsRequired = true, Order = 1)]
        public string TableName { get; set; }

        /// <summary>
        /// Gets or Sets the schema name.
        /// </summary>
        [DataMember(Name = "sn", IsRequired = false, EmitDefaultValue = false, Order = 2)]
        public string SchemaName { get; set; }

        /// <summary>
        /// Gets or Sets the table columns collection.
        /// </summary>
        [DataMember(Name = "cols", IsRequired = false, EmitDefaultValue = false, Order = 3)]
        public SetupColumns Columns { get; set; }

        /// <summary>
        /// Gets or Sets the Sync direction (may be Bidirectional, DownloadOnly, UploadOnly)
        /// Default is Bidirectional.
        /// </summary>
        [DataMember(Name = "sd", IsRequired = false, EmitDefaultValue = false, Order = 4)]
        public SyncDirection SyncDirection { get; set; }

        /// <summary>
        /// Gets a value indicating whether check if SetupTable has columns. If not columns specified, all the columns from server database are retrieved.
        /// </summary>
        [IgnoreDataMember]
        public bool HasColumns => this.Columns?.Count > 0;

        /// <summary>
        /// Initializes a new instance of the <see cref="SetupTable"/> class.
        /// Specify a table to add to the sync process
        /// If you don't specify any columns, all columns in the data source will be imported.
        /// </summary>
        public SetupTable(string tableName, string schemaName = null)
        {
            this.TableName = tableName ?? throw new ArgumentNullException(nameof(tableName));

            // Potentially user can pass something like [SalesLT].[Product]
            // or SalesLT.Product or Product. ParserName will handle it
            var parserTableName = ParserName.Parse(this.TableName);
            this.TableName = parserTableName.ObjectName;

            // Check Schema
            if (string.IsNullOrEmpty(schemaName))
            {
                schemaName = string.IsNullOrEmpty(parserTableName.SchemaName) ? null : parserTableName.SchemaName;
            }
            else
            {
                var parserSchemaName = ParserName.Parse(schemaName);
                schemaName = parserSchemaName.ObjectName;
            }

            // https://github.com/Mimetis/Dotmim.Sync/issues/621#issuecomment-968369322
            this.SchemaName = string.IsNullOrEmpty(schemaName) ? string.Empty : schemaName;
            this.Columns = [];
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SetupTable"/> class.
        /// Specify a table and its columns, to add to the sync process
        /// If you're specifying some columns, all others columns in the data source will be ignored.
        /// </summary>
        public SetupTable(string tableName, IEnumerable<string> columnsName, string schemaName = null)
            : this(tableName, schemaName) => this.Columns.AddRange(columnsName);

        /// <summary>
        /// ToString override. Gets the full name + columns count.
        /// </summary>
        public override string ToString() => this.GetFullName() + (this.HasColumns ? $" ({this.Columns.Count} columns)" : string.Empty);

        /// <summary>
        /// Gets the full name of the table, based on schema name + "." + table name (if schema name exists).
        /// </summary>
        /// <returns></returns>
        public string GetFullName()
            => string.IsNullOrEmpty(this.SchemaName) ? this.TableName : $"{this.SchemaName}.{this.TableName}";

        /// <inheritdoc cref="SyncNamedItem{T}.EqualsByProperties(T)"/>
        public override bool EqualsByProperties(SetupTable otherInstance)
        {
            if (otherInstance == null)
                return false;

            var sc = SyncGlobalization.DataSourceStringComparison;

            if (!this.EqualsByName(otherInstance))
                return false;

            // checking properties
            if (this.SyncDirection != otherInstance.SyncDirection)
                return false;

            if (!this.Columns.CompareWith(otherInstance.Columns, (c, oc) => string.Equals(c, oc, sc)))
                return false;

            return true;
        }

        /// <inheritdoc cref="SyncNamedItem{T}.GetAllNamesProperties"/>
        public override IEnumerable<string> GetAllNamesProperties()
        {
            yield return this.TableName;
            yield return this.SchemaName;
        }
    }
}