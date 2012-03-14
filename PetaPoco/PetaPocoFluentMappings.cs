using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.RegularExpressions;

namespace PetaPoco.FluentMappings
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

            TableInfo.AutoIncrement = typeConfig.AutoIncrement ?? true;

            // Set autoincrement false if primary key has multiple columns
            TableInfo.AutoIncrement = TableInfo.AutoIncrement ? !TableInfo.PrimaryKey.Contains(',') : TableInfo.AutoIncrement;

            // Call column mapper
            if (Database.Mapper != null)
                Database.Mapper.GetTableInfo(t, TableInfo);

            // Work out bound properties
            bool explicitColumns = typeConfig.ExplicitColumns ?? false;
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
                    if (isColumnDefined && (typeConfig.ColumnConfiguration[pi.Name].IgnoreColumn.HasValue && typeConfig.ColumnConfiguration[pi.Name].IgnoreColumn.Value))
                        continue;
                }

                var pc = new Database.PocoColumn();
                pc.PropertyInfo = pi;

                // Work out the DB column name
                if (isColumnDefined)
                {
                    var colattr = typeConfig.ColumnConfiguration[pi.Name];
                    pc.ColumnName = colattr.DbColumnName;
                    if (colattr.ResultColumn.HasValue && colattr.ResultColumn.Value)
                        pc.ResultColumn = true;
                    else if (colattr.VersionColumn.HasValue && colattr.VersionColumn.Value)
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

    public class FluentMappingConfiguration
    {
        public static void Configure(params IPetaPocoMap[] petaPocoMaps)
        {
            var mappings = PetaPocoMappings.BuildMappingsFromMaps(petaPocoMaps);
            SetFactory(mappings, null);
        }

        public static void Configure(PetaPocoMappings mappings)
        {
            SetFactory(mappings, null);
        }

        public static PetaPocoMappings Scan(Action<IPetaPocoConventionScanner> scanner)
        {
            var scannerSettings = ProcessSettings(scanner);
            if (scannerSettings.Lazy)
            {
                var lazyPetaPocoMappings = new PetaPocoMappings();
                SetFactory(lazyPetaPocoMappings, scanner);
                return lazyPetaPocoMappings;
            }
            
            return CreateMappings(scannerSettings, null);
        }

        private static PetaPocoMappings CreateMappings(PetaPocoConventionScannerSettings scannerSettings, Type[] typesOverride)
        {
            var types = typesOverride ?? FindTypes(scannerSettings);

            var config = new Dictionary<Type, PetaPocoTypeDefinition>();

            foreach (var type in types)
            {
                var petaPocoDefn = new PetaPocoTypeDefinition(type)
                {
                    AutoIncrement = scannerSettings.PrimaryKeysAutoIncremented(type),
                    PrimaryKey = scannerSettings.PrimaryKeysNamed(type),
                    TableName = scannerSettings.TablesNamed(type),
                    SequenceName = scannerSettings.SequencesNamed(type),
                };

                foreach (var prop in type.GetProperties())
                {
                    var column = new PetaPocoColumnDefinition();
                    column.PropertyInfo = prop;
                    column.DbColumnName = scannerSettings.PropertiesNamed(prop);
                    column.IgnoreColumn = scannerSettings.IgnorePropertiesWhere.Any(x => x.Invoke(prop));
                    column.ResultColumn = scannerSettings.ResultPropertiesWhere(prop);
                    column.VersionColumn = scannerSettings.VersionPropertiesWhere(prop);
                    petaPocoDefn.ColumnConfiguration.Add(prop.Name, column);
                }

                config.Add(type, petaPocoDefn);
            }

            MergeOverrides(config, scannerSettings.MappingOverrides);

            var petaPocoMappings = new PetaPocoMappings {Config = config};
            SetFactory(petaPocoMappings, null);
            return petaPocoMappings;
        }

        private static PetaPocoConventionScannerSettings ProcessSettings(Action<IPetaPocoConventionScanner> scanner)
        {
            var defaultScannerSettings = new PetaPocoConventionScannerSettings
            {
                PrimaryKeysAutoIncremented = x => true,
                PrimaryKeysNamed = x => "ID",
                TablesNamed = x => x.Name,
                PropertiesNamed = x => x.Name,
                ResultPropertiesWhere = x => false,
                VersionPropertiesWhere = x => false,
                SequencesNamed = x => null,
                Lazy = false
            };

            scanner.Invoke(new PetaPocoConventionScanner(defaultScannerSettings));
            return defaultScannerSettings;
        }

        private static IEnumerable<Type> FindTypes(PetaPocoConventionScannerSettings scannerSettings)
        {
            if (scannerSettings.TheCallingAssembly)
                scannerSettings.Assemblies.Add(FindTheCallingAssembly());

            var types = scannerSettings.Assemblies
                .SelectMany(x => x.GetExportedTypes())
                .Where(x => scannerSettings.IncludeTypes.All(y => y.Invoke(x)))
                .Where(x => !x.IsNested && !typeof (PetaPocoMap<>).IsAssignableFrom(x) && !typeof (PetaPocoMappings).IsAssignableFrom(x));
            return types;
        }

        private static void MergeOverrides(Dictionary<Type, PetaPocoTypeDefinition> config, PetaPocoMappings overrideMappings)
        {
            if (overrideMappings == null)
                return;

            foreach (var overrideTypeDefinition in overrideMappings.Config)
            {
                if (!config.ContainsKey(overrideTypeDefinition.Key))
                    continue;

                var convTableDefinition = config[overrideTypeDefinition.Key];

                convTableDefinition.PrimaryKey = overrideTypeDefinition.Value.PrimaryKey ?? convTableDefinition.PrimaryKey;
                convTableDefinition.SequenceName = overrideTypeDefinition.Value.SequenceName ?? convTableDefinition.SequenceName;
                convTableDefinition.TableName = overrideTypeDefinition.Value.TableName ?? convTableDefinition.TableName;
                convTableDefinition.AutoIncrement = overrideTypeDefinition.Value.AutoIncrement ?? convTableDefinition.AutoIncrement;
                convTableDefinition.ExplicitColumns = overrideTypeDefinition.Value.ExplicitColumns ?? convTableDefinition.ExplicitColumns;

                foreach (var overrideColumnDefinition in overrideMappings.Config[overrideTypeDefinition.Key].ColumnConfiguration)
                {
                    var convColDefinition = convTableDefinition.ColumnConfiguration[overrideColumnDefinition.Key];

                    convColDefinition.DbColumnName = overrideColumnDefinition.Value.DbColumnName ?? convColDefinition.DbColumnName;
                    convColDefinition.IgnoreColumn = overrideColumnDefinition.Value.IgnoreColumn ?? convColDefinition.IgnoreColumn;
                    convColDefinition.ResultColumn = overrideColumnDefinition.Value.ResultColumn ?? convColDefinition.ResultColumn;
                    convColDefinition.VersionColumn = overrideColumnDefinition.Value.VersionColumn ?? convColDefinition.VersionColumn;
                    convColDefinition.PropertyInfo = overrideColumnDefinition.Value.PropertyInfo ?? convColDefinition.PropertyInfo;    
                }
            }
        }

        private static void SetFactory(PetaPocoMappings mappings, Action<IPetaPocoConventionScanner> scanner)
        {
            var maps = mappings;
            var scana = scanner;
            Database.PocoDataFactory = t =>
            {
                if (maps != null)
                {
                    if (maps.Config.ContainsKey(t))
                    {
                        return new FluentMappingsPocoData(t, mappings.Config[t]);
                    }

                    if (scana != null)
                    {
                        var settings = ProcessSettings(scana);
                        var typeMapping = CreateMappings(settings, new[] { t });
                        return new FluentMappingsPocoData(t, typeMapping.Config[t]);
                    }
                }
                return new Database.PocoData(t);
            };
        }

        // Helper method if code is in seperate assembly
        private static Assembly FindTheCallingAssembly()
        {
            if (!typeof(FluentMappingConfiguration).Assembly.FullName.StartsWith("PetaPoco,"))
                return Assembly.GetCallingAssembly();

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
            IgnorePropertiesWhere = new List<Func<PropertyInfo, bool>>();
            IncludeTypes = new List<Func<Type, bool>>();
        }

        public PetaPocoMappings MappingOverrides { get; set; }

        public HashSet<Assembly> Assemblies { get; set; }
        public bool TheCallingAssembly { get; set; }
        public List<Func<Type, bool>> IncludeTypes { get; set; }

        public Func<Type, string> TablesNamed { get; set; }
        public Func<Type, string> PrimaryKeysNamed { get; set; }
        public Func<Type, bool> PrimaryKeysAutoIncremented { get; set; }
        public Func<Type, string> SequencesNamed { get; set; }

        public Func<PropertyInfo, string> PropertiesNamed { get; set; }
        public List<Func<PropertyInfo, bool>> IgnorePropertiesWhere { get; set; }
        public Func<PropertyInfo, bool> VersionPropertiesWhere { get; set; }
        public Func<PropertyInfo, bool> ResultPropertiesWhere { get; set; }

        public bool Lazy { get; set; }
    }

    public interface IPetaPocoConventionScanner
    {
        void OverrideMappingsWith(PetaPocoMappings mappings);
        void OverrideMappingsWith(params IPetaPocoMap[] maps);

        void Assembly(Assembly assembly);
        void TheCallingAssembly();
        void IncludeTypes(Func<Type, bool> includeTypes);

        void TablesNamed(Func<Type, string> tableFunc);
        void PrimaryKeysNamed(Func<Type, string> primaryKeyFunc);
        void PrimaryKeysAutoIncremented(Func<Type, bool> primaryKeyAutoIncrementFunc);
        void SequencesNamed(Func<Type, string> sequencesFunc);

        void LazyLoadMappings();

        IColumnsBuilderConventions Columns { get; }
    }

    public class PetaPocoConventionScanner : IPetaPocoConventionScanner
    {
        private readonly PetaPocoConventionScannerSettings _scannerSettings;

        public PetaPocoConventionScanner(PetaPocoConventionScannerSettings scannerSettings)
        {
            _scannerSettings = scannerSettings;
        }

        public void OverrideMappingsWith(PetaPocoMappings mappings)
        {
            _scannerSettings.MappingOverrides = mappings;
        }

        public void OverrideMappingsWith(params IPetaPocoMap[] maps)
        {
            var mappings = PetaPocoMappings.BuildMappingsFromMaps(maps);
            _scannerSettings.MappingOverrides = mappings;
        }

        public void Assembly(Assembly assembly)
        {
            _scannerSettings.Assemblies.Add(assembly);
        }

        public void TheCallingAssembly()
        {
            _scannerSettings.TheCallingAssembly = true;
        }

        public void IncludeTypes(Func<Type, bool> typeIncludes)
        {
            _scannerSettings.IncludeTypes.Add(typeIncludes);
        }

        public void TablesNamed(Func<Type, string> tableFunc)
        {
            _scannerSettings.TablesNamed = tableFunc;
        }

        public void PrimaryKeysNamed(Func<Type, string> primaryKeyFunc)
        {
            _scannerSettings.PrimaryKeysNamed = primaryKeyFunc;
        }

        public void SequencesNamed(Func<Type, string> sequencesFunc)
        {
            _scannerSettings.SequencesNamed = sequencesFunc;
        }

        public void LazyLoadMappings()
        {
            _scannerSettings.Lazy = true;
        }

        public void PrimaryKeysAutoIncremented(Func<Type, bool> primaryKeyAutoIncrementFunc)
        {
            _scannerSettings.PrimaryKeysAutoIncremented = primaryKeyAutoIncrementFunc;
        }

        public IColumnsBuilderConventions Columns
        {
            get { return new PropertyBuilderConventions(_scannerSettings); }
        }
    }

    public interface IColumnsBuilderConventions
    {
        IColumnsBuilderConventions Named(Func<PropertyInfo, string> propertiesNamedFunc);
        IColumnsBuilderConventions IgnoreWhere(Func<PropertyInfo, bool> ignorePropertiesWhereFunc);
        IColumnsBuilderConventions ResultWhere(Func<PropertyInfo, bool> resultPropertiesWhereFunc);
        IColumnsBuilderConventions VersionWhere(Func<PropertyInfo, bool> versionPropertiesWhereFunc);
    }

    public static class PetaPocoConventionExtensions
    {
        public static IColumnsBuilderConventions IgnoreComplex(this IColumnsBuilderConventions conventions)
        {
            return conventions.IgnoreWhere(y => !(y.PropertyType.IsValueType || y.PropertyType == typeof(string) || y.PropertyType == typeof(byte[])));
        }

        public static void WithSmartConventions(this IPetaPocoConventionScanner scanner)
        {
            scanner.PrimaryKeysNamed(y => y.Name + "Id");
            scanner.TablesNamed(y => Inflector.MakePlural(y.Name));
            scanner.Columns.IgnoreComplex();
        }
    }

    public class PropertyBuilderConventions : IColumnsBuilderConventions
    {
        private readonly PetaPocoConventionScannerSettings _scannerSettings;

        public PropertyBuilderConventions(PetaPocoConventionScannerSettings scannerSettings)
        {
            _scannerSettings = scannerSettings;
        }

        public IColumnsBuilderConventions Named(Func<PropertyInfo, string> propertiesNamedFunc)
        {
            _scannerSettings.PropertiesNamed = propertiesNamedFunc;
            return this;
        }

        public IColumnsBuilderConventions IgnoreWhere(Func<PropertyInfo, bool> ignorePropertiesWhereFunc)
        {
            _scannerSettings.IgnorePropertiesWhere.Add(ignorePropertiesWhereFunc);
            return this;
        }

        public IColumnsBuilderConventions ResultWhere(Func<PropertyInfo, bool> resultPropertiesWhereFunc)
        {
            _scannerSettings.ResultPropertiesWhere = resultPropertiesWhereFunc;
            return this;
        }

        public IColumnsBuilderConventions VersionWhere(Func<PropertyInfo, bool> versionPropertiesWhereFunc)
        {
            _scannerSettings.VersionPropertiesWhere = versionPropertiesWhereFunc;
            return this;
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
            SetColumnDefinition(property, dbColumnName, null, null, null);
        }

        public void Result(Expression<Func<T, object>> property)
        {
            Result(property, null);
        }

        public void Result(Expression<Func<T, object>> property, string dbColumnName)
        {
            SetColumnDefinition(property, dbColumnName, null, true, null);
        }

        public void Ignore(Expression<Func<T, object>> property)
        {
            SetColumnDefinition(property, null, true, null, null);
        }

        public void Version(Expression<Func<T, object>> property)
        {
            Version(property, null);
        }

        public void Version(Expression<Func<T, object>> property, string dbColumnName)
        {
            SetColumnDefinition(property, dbColumnName, null, null, true);
        }

        private void SetColumnDefinition(Expression<Func<T, object>> property, string dbColumnName, bool? ignoreColumn, bool? resultColumn, bool? versionColumn) 
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
        }

        public PetaPocoMap<T> TableName(string tableName)
        {
            _petaPocoTypeDefinition.TableName = tableName;
            return this;
        }

        public PetaPocoMap<T> Columns(Action<PetaPocoColumnConfigurationBuilder<T>> columnConfiguration)
        {
            return Columns(columnConfiguration, null);
        }

        public PetaPocoMap<T> Columns(Action<PetaPocoColumnConfigurationBuilder<T>> columnConfiguration, bool? explicitColumns)
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
            _petaPocoTypeDefinition.AutoIncrement = true;
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
        public bool? AutoIncrement { get; set; }
        public bool? ExplicitColumns { get; set; }
        public Dictionary<string, PetaPocoColumnDefinition> ColumnConfiguration { get; set; }
    }

    public class PetaPocoColumnDefinition
    {
        public PropertyInfo PropertyInfo { get; set; }
        public string DbColumnName { get; set; }
        public bool? ResultColumn { get; set; }
        public bool? IgnoreColumn { get; set; }
        public bool? VersionColumn { get; set; }
    }

    public static class Inflector
    {
        private static readonly List<InflectorRule> _plurals = new List<InflectorRule>();
        private static readonly List<InflectorRule> _singulars = new List<InflectorRule>();
        private static readonly List<string> _uncountables = new List<string>();

        static Inflector()
        {
            AddPluralRule("$", "s");
            AddPluralRule("s$", "s");
            AddPluralRule("(ax|test)is$", "$1es");
            AddPluralRule("(octop|vir)us$", "$1i");
            AddPluralRule("(alias|status)$", "$1es");
            AddPluralRule("(bu)s$", "$1ses");
            AddPluralRule("(buffal|tomat)o$", "$1oes");
            AddPluralRule("([ti])um$", "$1a");
            AddPluralRule("sis$", "ses");
            AddPluralRule("(?:([^f])fe|([lr])f)$", "$1$2ves");
            AddPluralRule("(hive)$", "$1s");
            AddPluralRule("([^aeiouy]|qu)y$", "$1ies");
            AddPluralRule("(x|ch|ss|sh)$", "$1es");
            AddPluralRule("(matr|vert|ind)ix|ex$", "$1ices");
            AddPluralRule("([m|l])ouse$", "$1ice");
            AddPluralRule("^(ox)$", "$1en");
            AddPluralRule("(quiz)$", "$1zes");

            AddSingularRule("s$", String.Empty);
            AddSingularRule("ss$", "ss");
            AddSingularRule("(n)ews$", "$1ews");
            AddSingularRule("([ti])a$", "$1um");
            AddSingularRule("((a)naly|(b)a|(d)iagno|(p)arenthe|(p)rogno|(s)ynop|(t)he)ses$", "$1$2sis");
            AddSingularRule("(^analy)ses$", "$1sis");
            AddSingularRule("([^f])ves$", "$1fe");
            AddSingularRule("(hive)s$", "$1");
            AddSingularRule("(tive)s$", "$1");
            AddSingularRule("([lr])ves$", "$1f");
            AddSingularRule("([^aeiouy]|qu)ies$", "$1y");
            AddSingularRule("(s)eries$", "$1eries");
            AddSingularRule("(m)ovies$", "$1ovie");
            AddSingularRule("(x|ch|ss|sh)es$", "$1");
            AddSingularRule("([m|l])ice$", "$1ouse");
            AddSingularRule("(bus)es$", "$1");
            AddSingularRule("(o)es$", "$1");
            AddSingularRule("(shoe)s$", "$1");
            AddSingularRule("(cris|ax|test)es$", "$1is");
            AddSingularRule("(octop|vir)i$", "$1us");
            AddSingularRule("(alias|status)$", "$1");
            AddSingularRule("(alias|status)es$", "$1");
            AddSingularRule("^(ox)en", "$1");
            AddSingularRule("(vert|ind)ices$", "$1ex");
            AddSingularRule("(matr)ices$", "$1ix");
            AddSingularRule("(quiz)zes$", "$1");

            AddIrregularRule("person", "people");
            AddIrregularRule("man", "men");
            AddIrregularRule("child", "children");
            AddIrregularRule("sex", "sexes");
            AddIrregularRule("tax", "taxes");
            AddIrregularRule("move", "moves");

            AddUnknownCountRule("equipment");
            AddUnknownCountRule("information");
            AddUnknownCountRule("rice");
            AddUnknownCountRule("money");
            AddUnknownCountRule("species");
            AddUnknownCountRule("series");
            AddUnknownCountRule("fish");
            AddUnknownCountRule("sheep");
        }

        private static void AddIrregularRule(string singular, string plural)
        {
            AddPluralRule(String.Concat("(", singular[0], ")", singular.Substring(1), "$"), String.Concat("$1", plural.Substring(1)));
            AddSingularRule(String.Concat("(", plural[0], ")", plural.Substring(1), "$"), String.Concat("$1", singular.Substring(1)));
        }

        private static void AddUnknownCountRule(string word)
        {
            _uncountables.Add(word.ToLower());
        }

        private static void AddPluralRule(string rule, string replacement)
        {
            _plurals.Add(new InflectorRule(rule, replacement));
        }

        private static void AddSingularRule(string rule, string replacement)
        {
            _singulars.Add(new InflectorRule(rule, replacement));
        }

        public static string MakePlural(string word)
        {
            return ApplyRules(_plurals, word);
        }

        public static string MakeSingular(string word)
        {
            return ApplyRules(_singulars, word);
        }

        private static string ApplyRules(IList<InflectorRule> rules, string word)
        {
            string result = word;
            if (!_uncountables.Contains(word.ToLower()))
            {
                for (int i = rules.Count - 1; i >= 0; i--)
                {
                    string currentPass = rules[i].Apply(word);
                    if (currentPass != null)
                    {
                        result = currentPass;
                        break;
                    }
                }
            }
            return result;
        }

        public static string ToTitleCase(string word)
        {
            return Regex.Replace(ToHumanCase(AddUnderscores(word)), @"\b([a-z])",
                delegate(Match match) { return match.Captures[0].Value.ToUpper(); });
        }

        public static string ToHumanCase(string lowercaseAndUnderscoredWord)
        {
            return MakeInitialCaps(Regex.Replace(lowercaseAndUnderscoredWord, @"_", " "));
        }

        public static string AddUnderscores(string pascalCasedWord)
        {
            return Regex.Replace(Regex.Replace(Regex.Replace(pascalCasedWord, @"([A-Z]+)([A-Z][a-z])", "$1_$2"), @"([a-z\d])([A-Z])", "$1_$2"), @"[-\s]", "_").ToLower();
        }

        public static string MakeInitialCaps(string word)
        {
            return String.Concat(word.Substring(0, 1).ToUpper(), word.Substring(1).ToLower());
        }

        public static string MakeInitialLowerCase(string word)
        {
            return String.Concat(word.Substring(0, 1).ToLower(), word.Substring(1));
        }

        public static bool IsStringNumeric(string str)
        {
            double result;
            return (double.TryParse(str, NumberStyles.Float, NumberFormatInfo.CurrentInfo, out result));
        }

        public static string AddOrdinalSuffix(string number)
        {
            if (IsStringNumeric(number))
            {
                int n = int.Parse(number);
                int nMod100 = n % 100;

                if (nMod100 >= 11 && nMod100 <= 13)
                    return String.Concat(number, "th");

                switch (n % 10)
                {
                    case 1:
                        return String.Concat(number, "st");
                    case 2:
                        return String.Concat(number, "nd");
                    case 3:
                        return String.Concat(number, "rd");
                    default:
                        return String.Concat(number, "th");
                }
            }
            return number;
        }

        public static string ConvertUnderscoresToDashes(string underscoredWord)
        {
            return underscoredWord.Replace('_', '-');
        }


        #region Nested type: InflectorRule

        private class InflectorRule
        {
            public readonly Regex regex;
            public readonly string replacement;

            public InflectorRule(string regexPattern, string replacementText)
            {
                regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
                replacement = replacementText;
            }

            public string Apply(string word)
            {
                if (!regex.IsMatch(word))
                    return null;

                string replace = regex.Replace(word, replacement);
                if (word == word.ToUpper())
                    replace = replace.ToUpper();

                return replace;
            }
        }

        #endregion
    }

}
