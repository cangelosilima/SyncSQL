using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace SyncSql.Extraction.MsSql.Tests;

internal sealed class FakeDatabase : DbConnection
{
    private ConnectionState _state;
    public Dictionary<string, DataTable> Results { get; } = new(StringComparer.Ordinal);
    public HashSet<string> FailQueries { get; } = new(StringComparer.Ordinal);
    public List<string> Queries { get; } = [];
    public bool FailOpen { get; set; }
    public bool WasDisposed { get; private set; }
    [AllowNull] public override string ConnectionString { get; set; } = "";
    public override string Database => "db";
    public override string DataSource => "fake";
    public override string ServerVersion => "16.0";
    public override ConnectionState State => _state;
    public override void Open()
    {
        if (FailOpen) { throw new FakeDatabaseException(); }
        _state = ConnectionState.Open;
    }
    public override void Close() => _state = ConnectionState.Closed;
    public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
    protected override DbCommand CreateDbCommand() => new FakeCommand(this);
    protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }

    public void Rows<T>(string sql, params T[] rows)
    {
        DataTable table = new() { Locale = CultureInfo.InvariantCulture };
        var properties = typeof(T).GetProperties();
        foreach (var property in properties)
        {
            table.Columns.Add(property.Name, Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType);
        }
        foreach (T row in rows)
        {
            table.Rows.Add(properties.Select(p => p.GetValue(row) ?? DBNull.Value).ToArray());
        }
        Results[sql] = table;
    }

    private sealed class FakeCommand(FakeDatabase database) : DbCommand
    {
        private readonly SqlCommand _parameters = new();
        [AllowNull] public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = database;
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => _parameters.Parameters;
        protected override DbParameter CreateDbParameter() => new SqlParameter();
        public override void Cancel() { }
        public override void Prepare() { }
        public override int ExecuteNonQuery() => throw new NotSupportedException();
        public override object? ExecuteScalar()
        {
            using var reader = ExecuteReader();
            return reader.Read() ? reader.GetValue(0) : null;
        }
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            database.Queries.Add(CommandText);
            if (database.FailQueries.Contains(CommandText)) { throw new FakeDatabaseException(); }
            if (database.Results.TryGetValue(CommandText, out var table)) { return table.CreateDataReader(); }
            throw new InvalidOperationException("Unexpected query: " + CommandText);
        }
        protected override void Dispose(bool disposing) { if (disposing) { _parameters.Dispose(); } base.Dispose(disposing); }
    }
}

internal sealed class FakeDatabaseException() : DbException("Database unavailable");
