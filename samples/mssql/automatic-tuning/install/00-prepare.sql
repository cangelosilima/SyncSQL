-- SyncSQL sample prelude - not part of microsoft/sql-server-samples.
--
-- report.sql is a CREATE PROCEDURE (it is a database-project source file, not an
-- install script), so it fails on a second run unless the procedure is gone first.
USE [WideWorldImporters];
GO

DROP PROCEDURE IF EXISTS [dbo].[report];
GO
