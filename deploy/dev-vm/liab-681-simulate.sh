#!/bin/bash
# Non-destructive: adds a 681 liability + its paired funded expense inside a transaction,
# reports every dashboard term before and after, then ROLLS BACK.
set -e
DB="${1:-SmmsDb_Aadya}"
PW=$(docker exec smms-api printenv ControlPlane__TenantConnectionTemplate | sed -n 's/.*Password=\([^;]*\).*/\1/p')

cat > /tmp/liab-sim.sql <<'SQL'
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @amt decimal(12,2) = 681.00;
DECLARE @dt  datetime2     = '2026-07-18';

DECLARE @A decimal(18,2), @B decimal(18,2), @C decimal(18,2), @D decimal(18,2), @E decimal(18,2), @L decimal(18,2);

SELECT @A = ISNULL(SUM(Amount),0) FROM Collections WHERE LOWER(Status)='paid';
SELECT @B = ISNULL(SUM(Amount),0) FROM SocietyIncomes WHERE IsDeleted=0;
SELECT @C = ISNULL(SUM(Amount),0) FROM Expenses WHERE IsDeleted=0;
SELECT @D = ISNULL(SUM(Amount),0) FROM Expenses WHERE IsDeleted=0 AND FundedByLiabilityId IS NOT NULL;
SELECT @E = ISNULL(SUM(s.Amount),0) FROM SocietyLiabilitySettlements s
     JOIN SocietyLiabilities l ON l.Id = s.LiabilityId
     WHERE s.Method='Repaid' AND EXISTS (SELECT 1 FROM Expenses e WHERE e.IsDeleted=0 AND e.FundedByLiabilityId=l.Id);
SELECT @L = ISNULL(SUM(Amount-SettledAmount),0) FROM SocietyLiabilities WHERE IsDeleted=0 AND Status<>'Settled';

PRINT '=== BEFORE ===';
PRINT 'A totalCol      = ' + CONVERT(varchar,@A);
PRINT 'B totalInc      = ' + CONVERT(varchar,@B);
PRINT 'C totalExp      = ' + CONVERT(varchar,@C);
PRINT 'D memberFunded  = ' + CONVERT(varchar,@D);
PRINT 'E repaidOut     = ' + CONVERT(varchar,@E);
PRINT 'CASH            = ' + CONVERT(varchar, @A + @B - (@C - @D) - @E);
PRINT 'BALANCE         = ' + CONVERT(varchar, @A + @B - @C);
PRINT 'OPEN LIABS      = ' + CONVERT(varchar,@L);

BEGIN TRANSACTION;

INSERT INTO SocietyLiabilities
  (Source, MemberId, ContributorName, Date, Amount, SettledAmount, Status, Purpose, CreatedOn, CreatedBy, IsDeleted, Category)
VALUES
  ('MemberContribution', 21, NULL, @dt, @amt, 0, 'Open', 'Funded by MR Rajesh', SYSUTCDATETIME(), 'sim', 0, 'Miscellaneous');

DECLARE @lid int = SCOPE_IDENTITY();

INSERT INTO Expenses
  (ExpenseDate, Category, Description, Vendor, Amount, PaymentMode, Month, Year, Remarks, CreatedBy, CreatedOn, IsDeleted, FundedByLiabilityId)
VALUES
  (@dt, 'Miscellaneous', 'Funded by MR Rajesh', NULL, @amt, NULL, 7, 2026,
   'Paid out of pocket by MR Rajesh - society owes this back', 'sim', SYSUTCDATETIME(), 0, @lid);

SELECT @A = ISNULL(SUM(Amount),0) FROM Collections WHERE LOWER(Status)='paid';
SELECT @B = ISNULL(SUM(Amount),0) FROM SocietyIncomes WHERE IsDeleted=0;
SELECT @C = ISNULL(SUM(Amount),0) FROM Expenses WHERE IsDeleted=0;
SELECT @D = ISNULL(SUM(Amount),0) FROM Expenses WHERE IsDeleted=0 AND FundedByLiabilityId IS NOT NULL;
SELECT @E = ISNULL(SUM(s.Amount),0) FROM SocietyLiabilitySettlements s
     JOIN SocietyLiabilities l ON l.Id = s.LiabilityId
     WHERE s.Method='Repaid' AND EXISTS (SELECT 1 FROM Expenses e WHERE e.IsDeleted=0 AND e.FundedByLiabilityId=l.Id);
SELECT @L = ISNULL(SUM(Amount-SettledAmount),0) FROM SocietyLiabilities WHERE IsDeleted=0 AND Status<>'Settled';

PRINT '=== AFTER (+681 liability, id ' + CONVERT(varchar,@lid) + ') ===';
PRINT 'A totalCol      = ' + CONVERT(varchar,@A);
PRINT 'B totalInc      = ' + CONVERT(varchar,@B);
PRINT 'C totalExp      = ' + CONVERT(varchar,@C);
PRINT 'D memberFunded  = ' + CONVERT(varchar,@D);
PRINT 'E repaidOut     = ' + CONVERT(varchar,@E);
PRINT 'CASH            = ' + CONVERT(varchar, @A + @B - (@C - @D) - @E);
PRINT 'BALANCE         = ' + CONVERT(varchar, @A + @B - @C);
PRINT 'OPEN LIABS      = ' + CONVERT(varchar,@L);

ROLLBACK TRANSACTION;

SELECT @C = ISNULL(SUM(Amount),0) FROM Expenses WHERE IsDeleted=0;
SELECT @D = ISNULL(SUM(Amount),0) FROM Expenses WHERE IsDeleted=0 AND FundedByLiabilityId IS NOT NULL;
SELECT @L = ISNULL(SUM(Amount-SettledAmount),0) FROM SocietyLiabilities WHERE IsDeleted=0 AND Status<>'Settled';
PRINT '=== AFTER ROLLBACK (must equal BEFORE) ===';
PRINT 'C totalExp      = ' + CONVERT(varchar,@C);
PRINT 'D memberFunded  = ' + CONVERT(varchar,@D);
PRINT 'OPEN LIABS      = ' + CONVERT(varchar,@L);
SQL

docker cp /tmp/liab-sim.sql sqlserver:/tmp/liab-sim.sql
docker exec sqlserver /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$PW" -C -d "$DB" -i /tmp/liab-sim.sql
docker exec sqlserver rm -f /tmp/liab-sim.sql
echo done
