using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace SyncSql.Lineage.MsSql.Tests;

public sealed class VisitorBoundaryTests
{
    private static SchemaObjectName Name(params string?[] parts)
    {
        SchemaObjectName result = new();
        foreach (string? part in parts) { result.Identifiers.Add(new Identifier { Value = part! }); }
        return result;
    }

    [Fact]
    public void PartialTree_MissingNamesAndExpressionsDoNotInventReferences()
    {
        var visitor = new TSqlLineageVisitor();
        visitor.Visit(new NamedTableReference());
        visitor.Visit(new NamedTableReference { SchemaObject = Name() });
        visitor.Visit(new NamedTableReference { SchemaObject = Name((string?)null) });
        visitor.Visit(new ForeignKeyConstraintDefinition());
        visitor.Visit(new TriggerObject());
        visitor.Visit(new QueueProcedureOption());
        visitor.Visit(new FunctionCall());
        visitor.Visit(new FunctionCall { FunctionName = new Identifier() });
        visitor.Visit(new FunctionCall { FunctionName = new Identifier { Value = "f" }, CallTarget = new MultiPartIdentifierCallTarget() });
        visitor.Visit(new FunctionCall { FunctionName = new Identifier { Value = "f" }, CallTarget = new MultiPartIdentifierCallTarget { MultiPartIdentifier = new MultiPartIdentifier() } });
        visitor.Visit(new ExecuteStatement());
        visitor.Visit(new ExecuteStatement { ExecuteSpecification = new ExecuteSpecification() });
        visitor.Visit(new ExecuteStatement { ExecuteSpecification = new ExecuteSpecification { ExecutableEntity = new ExecutableProcedureReference() } });
        visitor.Visit(new ExecuteStatement { ExecuteSpecification = new ExecuteSpecification { ExecutableEntity = new ExecutableProcedureReference { ProcedureReference = new ProcedureReferenceName() } } });
        visitor.Visit(new ExecuteStatement { ExecuteSpecification = new ExecuteSpecification { ExecutableEntity = new ExecutableProcedureReference { ProcedureReference = new ProcedureReferenceName { ProcedureReference = new ProcedureReference() } } } });
        visitor.Visit(new DeleteStatement());
        visitor.Visit(new UpdateStatement());
        visitor.Visit(new OpenQueryTableReference());
        visitor.Visit(new OpenRowsetTableReference());
        visitor.Visit(new CommonTableExpression());
        visitor.Visit(new CommonTableExpression { ExpressionName = new Identifier() });
        visitor.Visit(new CommonTableExpression { ExpressionName = new Identifier { Value = " " } });
        visitor.Visit(new ColumnReferenceExpression());
        visitor.Visit(new ContractMessage());
        visitor.Visit(new ServiceContract());
        visitor.Visit(new CreateServiceStatement());
        visitor.Visit(new CreateServiceStatement { QueueName = Name() });
        visitor.Visit(new ReceiveStatement());
        visitor.Visit(new SendStatement());
        visitor.Visit(new SendStatement { MessageTypeName = new IdentifierOrValueExpression() });
        visitor.Visit(new SendStatement { MessageTypeName = new IdentifierOrValueExpression { ValueExpression = new IntegerLiteral { Value = "1" } } });
        visitor.Visit(new SetVariableStatement { Expression = new BinaryExpression { BinaryExpressionType = BinaryExpressionType.Add } });
        visitor.Visit(new SetVariableStatement { Expression = new BinaryExpression { BinaryExpressionType = BinaryExpressionType.Subtract } });
        visitor.Visit(new ExecuteStatement { ExecuteSpecification = new ExecuteSpecification { ExecutableEntity = new ExecutableStringList() } });
        Assert.Empty(visitor.ObjectRefs);
        Assert.Empty(visitor.ColumnRefs);
        Assert.Empty(visitor.CommonTableExpressionNames);
    }

    [Fact]
    public void PartialAliasTargets_KeepQualifiedTargetsAndTolerateMissingFrom()
    {
        var visitor = new TSqlLineageVisitor();
        foreach (var target in new TableReference[]
        {
            new VariableTableReference(), new NamedTableReference(), new NamedTableReference { SchemaObject = Name() },
            new NamedTableReference { SchemaObject = Name("db", "", "t") },
            new NamedTableReference { SchemaObject = Name("remote", "", "", "t") },
            new NamedTableReference { SchemaObject = Name("t") },
        })
        {
            visitor.Visit(new DeleteStatement { DeleteSpecification = new DeleteSpecification { Target = target } });
        }
        FromClause from = new();
        from.TableReferences.Add(new NamedTableReference { Alias = new Identifier { Value = " " } });
        from.TableReferences.Add(new NamedTableReference { Alias = new Identifier() });
        visitor.Visit(new DeleteStatement { DeleteSpecification = new DeleteSpecification { Target = new NamedTableReference { SchemaObject = Name("t") }, FromClause = from } });
        Assert.Empty(visitor.ObjectRefs);
    }

    [Fact]
    public void Broker_RecordsActivationAndLiteralNamesAndHonorsInstanceScope()
    {
        Guid local = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var visitor = new TSqlLineageVisitor(serviceBrokerGuid: local);
        visitor.Visit(new QueueProcedureOption { OptionValue = Name("dbo", "activate") });
        visitor.Visit(new ReceiveStatement { Queue = Name("queue") });
        visitor.Visit(new SendStatement { MessageTypeName = new IdentifierOrValueExpression { ValueExpression = new StringLiteral { Value = "message" } } });
        foreach (ValueExpression instance in new ValueExpression[] { new VariableReference { Name = "@instance" }, new StringLiteral { Value = "invalid" }, new StringLiteral { Value = Guid.Empty.ToString() } })
        {
            visitor.Visit(new BeginDialogStatement { TargetServiceName = new StringLiteral { Value = "remote" }, InstanceSpec = instance });
        }
        visitor.Visit(new BeginDialogStatement { TargetServiceName = new StringLiteral { Value = "local" }, InstanceSpec = new StringLiteral { Value = local.ToString() } });
        Assert.Contains(visitor.ObjectRefs, r => r.Name == "activate");
        Assert.Contains(visitor.ObjectRefs, r => r.Name == "message");
        Assert.Contains(visitor.ObjectRefs, r => r.Name == "local");
        Assert.DoesNotContain(visitor.ObjectRefs, r => r.Name == "remote");
    }

    [Fact]
    public void FunctionAndRemoteExecution_PreserveEveryQualifier()
    {
        var visitor = new TSqlLineageVisitor();
        MultiPartIdentifier qualifiers = new();
        foreach (string value in new[] { "remote", "db", "dbo" }) { qualifiers.Identifiers.Add(new Identifier { Value = value }); }
        visitor.Visit(new FunctionCall { FunctionName = new Identifier { Value = "fn" }, CallTarget = new MultiPartIdentifierCallTarget { MultiPartIdentifier = qualifiers } });
        var procedure = new ExecutableProcedureReference { ProcedureReference = new ProcedureReferenceName { ProcedureReference = new ProcedureReference { Name = Name("dbo", "run") } } };
        visitor.Visit(new ExecuteStatement { ExecuteSpecification = new ExecuteSpecification { ExecutableEntity = procedure, LinkedServer = new Identifier { Value = "remote" } } });
        Assert.Contains(visitor.ObjectRefs, r => r.Name == "fn" && r.Server == "remote" && r.Database == "db");
        Assert.Contains(visitor.ObjectRefs, r => r.Name == "run" && r.Server == "remote");
        visitor.Visit(new OpenRowsetTableReference { Query = new StringLiteral { Value = "SELECT * FROM dbo.orders" } });
        Assert.Contains(visitor.ObjectRefs, r => r.Name == "orders");
    }

    [Fact]
    public void ReplicationSource_RequiresLiteralOwnerAndObject()
    {
        var visitor = new TSqlLineageVisitor();
        foreach (SchemaObjectName name in new[] { Name("sp_addarticle"), Name("dbo", "sp_addarticle"), Name("db", "sys", "sp_addarticle"), Name("sys", "sp_addarticle"), Name("#temporary") })
        {
            var procedure = new ExecutableProcedureReference { ProcedureReference = new ProcedureReferenceName { ProcedureReference = new ProcedureReference { Name = name } } };
            procedure.Parameters.Add(new ExecuteParameter { Variable = new VariableReference { Name = "@source_owner" }, ParameterValue = new StringLiteral { Value = "dbo" } });
            procedure.Parameters.Add(new ExecuteParameter { Variable = new VariableReference { Name = "@source_object" }, ParameterValue = new VariableReference { Name = "@table" } });
            var execute = new ExecuteStatement { ExecuteSpecification = new ExecuteSpecification { ExecutableEntity = procedure } };
            visitor.Visit(execute);
            execute.ExecuteSpecification.LinkedServer = new Identifier { Value = "remote" };
            visitor.Visit(execute);
        }
        var positional = new ExecutableProcedureReference { ProcedureReference = new ProcedureReferenceName { ProcedureReference = new ProcedureReference { Name = Name("sys", "sp_addarticle") } } };
        positional.Parameters.Add(new ExecuteParameter { ParameterValue = new StringLiteral { Value = "publication" } });
        visitor.Visit(new ExecuteStatement { ExecuteSpecification = new ExecuteSpecification { ExecutableEntity = positional } });
        Assert.DoesNotContain(visitor.ObjectRefs, r => r.Name == "@table" || r.Name.StartsWith('#'));
    }

    [Fact]
    public void DynamicExpressions_RespectConcatenationAndDepthLimits()
    {
        var visitor = new TSqlLineageVisitor();
        ScalarExpression expression = new StringLiteral { Value = "SELECT * FROM dbo.tooDeep" };
        for (int i = 0; i < 66; i++) { expression = new BinaryExpression { BinaryExpressionType = BinaryExpressionType.Concat, FirstExpression = expression, SecondExpression = new StringLiteral { Value = " " } }; }
        visitor.Visit(new SetVariableStatement { Expression = expression });
        Assert.Empty(visitor.ObjectRefs);
        var strings = new ExecutableStringList();
        strings.Strings.Add(new VariableReference { Name = "@sql" });
        strings.Strings.Add(new StringLiteral { Value = "SELECT * FROM dbo.orders" });
        visitor.Visit(new ExecuteStatement { ExecuteSpecification = new ExecuteSpecification { ExecutableEntity = strings } });
        Assert.Contains(visitor.ObjectRefs, r => r.Name == "orders");
    }
}
