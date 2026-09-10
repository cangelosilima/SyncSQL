-- SyncSQL sample prelude - not part of microsoft/sql-server-samples.
--
-- The graph demo scripts use unguarded CREATE SCHEMA / CREATE TABLE, so they only
-- succeed against a WideWorldImporters that does not already carry the demo's
-- objects. Dropping them here makes re-provisioning repeatable.
--
-- Edge tables are dropped before the node tables they connect, because an edge
-- table's constraints reference its endpoints.
USE [WideWorldImporters];
GO

DROP TABLE IF EXISTS [Edges].[Bought];
DROP TABLE IF EXISTS [Edges].[Friends];
DROP TABLE IF EXISTS [Nodes].[StockItems];
DROP TABLE IF EXISTS [Nodes].[Customers];
DROP TABLE IF EXISTS [Nodes].[Person];
GO

DROP SCHEMA IF EXISTS [Edges];
GO
DROP SCHEMA IF EXISTS [Nodes];
GO
