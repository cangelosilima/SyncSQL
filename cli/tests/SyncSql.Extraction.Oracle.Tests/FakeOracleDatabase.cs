using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using Oracle.ManagedDataAccess.Client;

namespace SyncSql.Extraction.Oracle.Tests;

internal sealed class FakeOracleDatabase : DbConnection
{
    private ConnectionState _state;
    public Func<string, IReadOnlyDictionary<string, object>, object?> Execute { get; set; } = (_, _) => throw new InvalidOperationException("Unexpected query");
    public List<string> Queries { get; } = [];
    public bool WasDisposed { get; private set; }
    [AllowNull] public override string ConnectionString { get; set; } = "";
    public override string Database => "APP";
    public override string DataSource => "fake";
    public override string ServerVersion => "23";
    public override ConnectionState State => _state;
    public override void Open() => _state = ConnectionState.Open;
    public override void Close() => _state = ConnectionState.Closed;
    public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
    protected override DbCommand CreateDbCommand() => new FakeCommand(this);
    protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }

    public static OracleException Error(int number) => (OracleException)typeof(OracleException)
        .GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(int), typeof(string), typeof(string), typeof(string), typeof(int)], null)!
        .Invoke([number, "Simulated database error", "", "", 0]);

    public static DataTable Rows<T>(params T[] rows)
    {
        DataTable table = new() { Locale = CultureInfo.InvariantCulture };
        var properties = typeof(T).GetProperties();
        foreach (var property in properties) { table.Columns.Add(property.Name, Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType); }
        foreach (T row in rows) { table.Rows.Add(properties.Select(p => p.GetValue(row) ?? DBNull.Value).ToArray()); }
        return table;
    }

    private sealed class FakeCommand(FakeOracleDatabase database) : DbCommand
    {
        private readonly OracleCommand _parameters = new();
        [AllowNull] public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = database;
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => _parameters.Parameters;
        protected override DbParameter CreateDbParameter() => new OracleParameter();
        public override void Cancel() { }
        public override void Prepare() { }
        private object? Run()
        {
            database.Queries.Add(CommandText);
            return database.Execute(CommandText, _parameters.Parameters.Cast<DbParameter>().ToDictionary(p => p.ParameterName, p => p.Value!));
        }
        public override int ExecuteNonQuery() { Run(); return 0; }
        public override object? ExecuteScalar() => Run();
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => ((DataTable)Run()!).CreateDataReader();
        protected override void Dispose(bool disposing) { if (disposing) { _parameters.Dispose(); } base.Dispose(disposing); }
    }
}
