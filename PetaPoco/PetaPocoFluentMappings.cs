using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace PetaPoco
{
    public class FluentMappingsPocoData : Database.PocoData
    {
        public FluentMappingsPocoData(Type t, FluentMappings.PetaPocoTypeDefinition typeConfig)
        {
            type = t;
            TableInfo = new TableInfo();

            // Get the table name
            var a = typeConfig.TableName ?? "";
            TableInfo.TableName = a.Length == 0 ? t.Name : a;

            // Get the primary key
            a = typeConfig.PrimaryKey ?? "";
            TableInfo.PrimaryKey = a.Length == 0 ? "ID" : a;

            a = typeConfig.SequenceName ?? "";
            TableInfo.SequenceName = a.Length == 0 ? null : a;

            TableInfo.AutoIncrement = typeConfig.AutoIncrement;

            // Set autoincrement false if primary key has multiple columns
            TableInfo.AutoIncrement = TableInfo.AutoIncrement ? !TableInfo.PrimaryKey.Contains(',') : TableInfo.AutoIncrement;

            // Call column mapper
            if (Database.Mapper != null)
                Database.Mapper.GetTableInfo(t, TableInfo);

            // Work out bound properties
            bool explicitColumns = typeConfig.ExplicitColumns;
            Columns = new Dictionary<string, Database.PocoColumn>(StringComparer.OrdinalIgnoreCase);
            foreach (var pi in t.GetProperties())
            {
                // Work out if properties is to be included
                var isColumnDefined = typeConfig.ColumnConfiguration.ContainsKey(pi.Name);
                if (explicitColumns)
                {
                    if (!isColumnDefined)
                        continue;
                }
                else
                {
                    if (isColumnDefined && typeConfig.ColumnConfiguration[pi.Name].IgnoreColumn)
                        continue;
                }

                var pc = new Database.PocoColumn();
                pc.PropertyInfo = pi;

                // Work out the DB column name
                if (isColumnDefined)
                {
                    var colattr = typeConfig.ColumnConfiguration[pi.Name];
                    pc.ColumnName = colattr.DbColumnName;
                    if (colattr.ResultColumn)
                        pc.ResultColumn = true;
                    else if (colattr.VersionColumn)
                        pc.VersionColumn = true;

                    // Support for composite keys needed
                    if (pc.ColumnName != null && pi.Name == TableInfo.PrimaryKey)
                        TableInfo.PrimaryKey = pc.ColumnName;

                }
                if (pc.ColumnName == null)
                {
                    pc.ColumnName = pi.Name;
                    if (Database.Mapper != null && !Database.Mapper.MapPropertyToColumn(pi, ref pc.ColumnName, ref pc.ResultColumn))
                        continue;
                }

                // Store it
                Columns.Add(pc.ColumnName, pc);
            }

            // Build column list for automatic select
            QueryColumns = (from c in Columns where !c.Value.ResultColumn select c.Key).ToArray();

        }
    }

}

namespace PetaPoco.FluentMappings
{
    public class Configuration
    {
        public static void Configure(params IPetaPocoMap[] petaPocoMaps)
        {
            var mappings = PetaPocoMappings.BuildMappingsFromMaps(petaPocoMaps);
            SetFactory(mappings);
        }

        public static void Configure(PetaPocoMappings mappings)
        {
            SetFactory(mappings);
        }

        public static void Scan(Action<IPetaPocoConventionScanner> scanner)
        {
            var scannerSettings = new PetaPocoConventionScannerSettings
            {
                PrimaryKeyAutoIncremented = x => true,
                PrimaryKeysNamed = x => "ID",
                TablesNamed = x => x.Name,
                ColumnsNamed = x => x.Name,
                NameSpace = x => true,
                IgnorePropertiesWhere = x => false,
                ResultPropertiesWhere = x => false,
                VersionPropertiesWhere = x => false
            };

            scanner(new PetaPocoConventionScanner(scannerSettings));

            if (scannerSettings.TheCallingAssembly)
                scannerSettings.Assemblies.Add(Assembly.GetCallingAssembly());

            var types = scannerSettings.Assemblies
                .SelectMany(y=>y.GetExportedTypes().Where(x => scannerSettings.NameSpace(x.Namespace)));

            var config = new Dictionary<Type, PetaPocoTypeDefinition>();

            foreach (var type in types)
            {
                var petaPocoDefn = new PetaPocoTypeDefinition(type)
                {
                    AutoIncrement = scannerSettings.PrimaryKeyAutoIncremented(type),
                    PrimaryKey = scannerSettings.PrimaryKeysNamed(type),
                    TableName = scannerSettings.TablesNamed(type)
                };

                foreach (var prop in type.GetProperties())
                {
                    var column = new PetaPocoColumnDefinition();
                    column.PropertyInfo = prop;
                    column.DbColumnName = scannerSettings.ColumnsNamed(prop);
                    column.IgnoreColumn = scannerSettings.IgnorePropertiesWhere(prop);
                    column.ResultColumn = scannerSettings.ResultPropertiesWhere(prop);
                    column.VersionColumn = scannerSettings.VersionPropertiesWhere(prop);
                    petaPocoDefn.ColumnConfiguration.Add(prop.Name, column);
                }

                config.Add(type, petaPocoDefn);
            }
            
            SetFactory(new PetaPocoMappings { Config = config });
        }

        private static void SetFactory(PetaPocoMappings mappings)
        {
            Database.PocoDataFactory = t => (mappings != null && mappings.Config.ContainsKey(t))
                    ? new FluentMappingsPocoData(t, mappings.Config[t])
                    : new Database.PocoData(t);
        }

        // Helper method if code is in seperate assembly
        private static Assembly FindTheCallingAssembly()
        {
            var trace = new StackTrace(false);

            Assembly thisAssembly = Assembly.GetExecutingAssembly();
            Assembly callingAssembly = null;
            for (int i = 0; i < trace.FrameCount; i++)
            {
                StackFrame frame = trace.GetFrame(i);
                Assembly assembly = frame.GetMethod().DeclaringType.Assembly;
                if (assembly != thisAssembly)
                {
                    callingAssembly = assembly;
                    break;
                }
            }
            return callingAssembly;
        }
    }

    public class PetaPocoConventionScannerSettings
    {
        public PetaPocoConventionScannerSettings()
        {
            Assemblies = new HashSet<Assembly>();
        }

        public HashSet<Assembly> Assemblies { get; set; }
        public Func<Type, string> TablesNamed { get; set; }
        public Func<Type, string> PrimaryKeysNamed { get; set; }
        public Func<string, bool> NameSpace { get; set; }
        public Func<Type, bool> PrimaryKeyAutoIncremented { get; set; }
        public Func<PropertyInfo, string> ColumnsNamed { get; set; }
        public Func<PropertyInfo, bool> IgnorePropertiesWhere { get; set; }
        public bool TheCallingAssembly { get; set; }
        public Func<PropertyInfo, bool> VersionPropertiesWhere { get; set; }
        public Func<PropertyInfo, bool> ResultPropertiesWhere { get; set; }
    }

    public interface IPetaPocoConventionScanner
    {
        void Assembly(Assembly assembly);
        void NameSpace(Func<string, bool> nameSpaceFunc);
        void TablesNamed(Func<Type, string> tableFunc);
        void PrimaryKeysNamed(Func<Type, string> primaryKeyFunc);
        void PrimaryAutoIncremented(Func<Type, bool> primaryKeyAutoIncrementFunc);
        void IgnorePropertiesWhere(Func<PropertyInfo, bool> ignoreColumnsWhereFunc);
        void ResultPropertiesWhere(Func<PropertyInfo, bool> resultColumnsWhereFunc);
        void VersionPropertiesWhere(Func<PropertyInfo, bool> versionColumnsWhereFunc);
        void TheCallingAssembly(); 
    }

    public class PetaPocoConventionScanner : IPetaPocoConventionScanner
    {
        private readonly PetaPocoConventionScannerSettings _scannerSettings;

        public PetaPocoConventionScanner(PetaPocoConventionScannerSettings scannerSettings)
        {
            _scannerSettings = scannerSettings;
        }

        public void Assembly(Assembly assembly)
        {
            _scannerSettings.Assemblies.Add(assembly);
        }

        public void IgnorePropertiesWhere(Func<PropertyInfo, bool> ignoreColumnsWhereFunc)
        {
            _scannerSettings.IgnorePropertiesWhere = ignoreColumnsWhereFunc;
        }

        public void ResultPropertiesWhere(Func<PropertyInfo, bool> resultColumnsWhereFunc)
        {
            _scannerSettings.ResultPropertiesWhere = resultColumnsWhereFunc;
        }

        public void VersionPropertiesWhere(Func<PropertyInfo, bool> versionColumnsWhereFunc)
        {
            _scannerSettings.VersionPropertiesWhere = versionColumnsWhereFunc;
        }

        public void TheCallingAssembly()
        {
            _scannerSettings.TheCallingAssembly = true;
        }

        public void NameSpace(Func<string, bool> nameSpaceFunc)
        {
            _scannerSettings.NameSpace = nameSpaceFunc;
        }

        public void TablesNamed(Func<Type, string> tableFunc)
        {
            _scannerSettings.TablesNamed = tableFunc;
        }

        public void PrimaryKeysNamed(Func<Type, string> primaryKeyFunc)
        {
            _scannerSettings.PrimaryKeysNamed = primaryKeyFunc;
        }

        public void PrimaryAutoIncremented(Func<Type, bool> primaryKeyAutoIncrementFunc)
        {
            _scannerSettings.PrimaryKeyAutoIncremented = primaryKeyAutoIncrementFunc;
        }
    }

    public class PetaPocoMappings
    {
        public Dictionary<Type, PetaPocoTypeDefinition> Config = new Dictionary<Type, PetaPocoTypeDefinition>();

        public PetaPocoMap<T> For<T>()
        {
            var definition = new PetaPocoTypeDefinition(typeof(T));
            var petaPocoMap = new PetaPocoMap<T>(definition);
            Config.Add(typeof(T), definition);
            return petaPocoMap;
        }

        public static PetaPocoMappings BuildMappingsFromMaps(params IPetaPocoMap[] petaPocoMaps)
        {
            var petaPocoConfig = new PetaPocoMappings();
            foreach (var petaPocoMap in petaPocoMaps)
            {
                var type = petaPocoMap.TypeDefinition.Type;
                petaPocoConfig.Config[type] = petaPocoMap.TypeDefinition;
            }
            return petaPocoConfig;
        }
    }
    
    public class PetaPocoColumnConfigurationBuilder<T>
    {
        private readonly Dictionary<string, PetaPocoColumnDefinition> _columnDefinitions;

        public PetaPocoColumnConfigurationBuilder(Dictionary<string, PetaPocoColumnDefinition> columnDefinitions)
        {
            _columnDefinitions = columnDefinitions;
        }

        public void Column(Expression<Func<T, object>> property)
        {
            Column(property, null);
        }

        public void Column(Expression<Func<T, object>> property, string dbColumnName)
        {
            SetColumnDefinition(property, dbColumnName, false, false, false);
        }

        public void Result(Expression<Func<T, object>> property)
        {
            Result(property, null);
        }

        public void Result(Expression<Func<T, object>> property, string dbColumnName)
        {
            SetColumnDefinition(property, dbColumnName, false, true, false);
        }

        public void Ignore(Expression<Func<T, object>> property)
        {
            SetColumnDefinition(property, null, true, false, false);
        }

        public void Version(Expression<Func<T, object>> property)
        {
            Version(property, null);
        }

        public void Version(Expression<Func<T, object>> property, string dbColumnName)
        {
            SetColumnDefinition(property, dbColumnName, false, false, true);
        }

        private void SetColumnDefinition(Expression<Func<T, object>> property, string dbColumnName, bool ignoreColumn, bool resultColumn, bool versionColumn) 
        {
            var propertyInfo = PropertyHelper<T>.GetProperty(property);
            _columnDefinitions[propertyInfo.Name] = new PetaPocoColumnDefinition
            {
                PropertyInfo = propertyInfo, 
                DbColumnName = dbColumnName,
                ResultColumn = resultColumn,
                IgnoreColumn = ignoreColumn,
                VersionColumn = versionColumn
            };
        }
    }

    public interface IPetaPocoMap
    {
        PetaPocoTypeDefinition TypeDefinition { get; }
    }

    public class PetaPocoMap<T> : IPetaPocoMap
    {
        private readonly PetaPocoTypeDefinition _petaPocoTypeDefinition;

        public PetaPocoMap() : this(new PetaPocoTypeDefinition(typeof(T)))
        {
        }

        public PetaPocoMap(PetaPocoTypeDefinition petaPocoTypeDefinition)
        {
            _petaPocoTypeDefinition = petaPocoTypeDefinition;
            _petaPocoTypeDefinition.AutoIncrement = true;
        }

        public PetaPocoMap<T> TableName(string tableName)
        {
            _petaPocoTypeDefinition.TableName = tableName;
            return this;
        }

        public PetaPocoMap<T> Columns(Action<PetaPocoColumnConfigurationBuilder<T>> columnConfiguration)
        {
            return Columns(columnConfiguration, false);
        }

        public PetaPocoMap<T> Columns(Action<PetaPocoColumnConfigurationBuilder<T>> columnConfiguration, bool explicitColumns)
        {
            _petaPocoTypeDefinition.ExplicitColumns = explicitColumns;
            columnConfiguration(new PetaPocoColumnConfigurationBuilder<T>(_petaPocoTypeDefinition.ColumnConfiguration));
            return this;
        }

        public PetaPocoMap<T> PrimaryKey(Expression<Func<T, object>> column, string sequenceName)
        {
            var propertyInfo = PropertyHelper<T>.GetProperty(column);
            return PrimaryKey(propertyInfo.Name, sequenceName);
        }

        public PetaPocoMap<T> PrimaryKey(Expression<Func<T, object>> column)
        {
            return PrimaryKey(column, null);
        }

        public PetaPocoMap<T> PrimaryKey(Expression<Func<T, object>> column, bool autoIncrement)
        {
            var propertyInfo = PropertyHelper<T>.GetProperty(column);
            return PrimaryKey(propertyInfo.Name, autoIncrement);
        }

        public PetaPocoMap<T> CompositePrimaryKey(params Expression<Func<T, object>>[] columns)
        {
            var columnNames = new string[columns.Length];
            for (int i = 0; i < columns.Length; i++)
            {
                columnNames[i] = PropertyHelper<T>.GetProperty(columns[i]).Name;
            }

            _petaPocoTypeDefinition.PrimaryKey = string.Join(",", columnNames);
            return this;
        }

        public PetaPocoMap<T> PrimaryKey(string primaryKeyColumn, bool autoIncrement)
        {
            _petaPocoTypeDefinition.PrimaryKey = primaryKeyColumn;
            _petaPocoTypeDefinition.AutoIncrement = autoIncrement;
            return this;
        }

        public PetaPocoMap<T> PrimaryKey(string primaryKeyColumn, string sequenceName)
        {
            _petaPocoTypeDefinition.PrimaryKey = primaryKeyColumn;
            _petaPocoTypeDefinition.SequenceName = sequenceName;
            return this;
        }

        public PetaPocoMap<T> PrimaryKey(string primaryKeyColumn)
        {
            return PrimaryKey(primaryKeyColumn, null);
        }

        PetaPocoTypeDefinition IPetaPocoMap.TypeDefinition
        {
            get { return _petaPocoTypeDefinition; }
        }
    }

    public static class PropertyHelper<T>
    {
        public static PropertyInfo GetProperty<TValue>(Expression<Func<T, TValue>> selector)
        {
            Expression body = selector;
            if (body is LambdaExpression)
            {
                body = ((LambdaExpression)body).Body;
            }
            if (body is UnaryExpression)
            {
                body = ((UnaryExpression)body).Operand;
            }
            switch (body.NodeType)
            {
                case ExpressionType.MemberAccess:
                    return (PropertyInfo)((MemberExpression)body).Member;
                default:
                    throw new InvalidOperationException();
            }
        }
    }

    public class PetaPocoTypeDefinition
    {
        public PetaPocoTypeDefinition(Type type)
        {
            Type = type;
            ColumnConfiguration = new Dictionary<string, PetaPocoColumnDefinition>();
        }

        public Type Type { get; set; }
        public string TableName { get; set; }
        public string PrimaryKey { get; set; }
        public string SequenceName { get; set; }
        public bool AutoIncrement { get; set; }
        public bool ExplicitColumns { get; set; }
        public Dictionary<string, PetaPocoColumnDefinition> ColumnConfiguration { get; set; }
    }

    public class PetaPocoColumnDefinition
    {
        public PropertyInfo PropertyInfo { get; set; }
        public string DbColumnName { get; set; }
        public bool ResultColumn { get; set; }
        public bool IgnoreColumn { get; set; }
        public bool VersionColumn { get; set; }
    }

}
